using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;
using BrowseLens.Core.Privacy;
using BrowseLens.Core.Repositories;
using BrowseLens.Core.TimeAccounting;

namespace BrowseLens.Core.Monitoring
{
    public class IngestionResult
    {
        public bool Accepted { get; set; }
        public string? RejectionReason { get; set; }
        public long? PersistedSessionId { get; set; }
        public bool WasRedacted { get; set; }
        public bool WasExcluded { get; set; }
    }

    public class MonitoringCoordinator
    {
        private readonly SessionStateMachine _stateMachine;
        private readonly IBrowsingSessionRepository _sessionRepo;
        private readonly ISettingsRepository _settingsRepo;
        private readonly IBrowserInstanceRepository _browserRepo;
        private readonly TimeSpan _heartbeatTimeout;
        private readonly object _lock = new object();

        private readonly Dictionary<string, DateTime> _lastSeenPerBrowser = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConnectionStatus> _statusPerBrowser = new Dictionary<string, ConnectionStatus>(StringComparer.OrdinalIgnoreCase);
        private long _currentOpenSessionDbId;

        public SessionStateMachine StateMachine => _stateMachine;
        public long CurrentOpenSessionDbId => _currentOpenSessionDbId;

        public event Action<string, ConnectionStatus>? ConnectionStatusChanged;
        public event Action<BrowsingSessionEntity>? SessionPersisted;

        public MonitoringCoordinator(
            SessionStateMachine stateMachine,
            IBrowsingSessionRepository sessionRepo,
            ISettingsRepository settingsRepo,
            IBrowserInstanceRepository browserRepo,
            TimeSpan? heartbeatTimeout = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));
            _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
            _browserRepo = browserRepo ?? throw new ArgumentNullException(nameof(browserRepo));
            _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(45);
        }

        public ConnectionStatus GetConnectionStatus(string browser)
        {
            lock (_lock)
            {
                if (_statusPerBrowser.TryGetValue(browser, out var status))
                {
                    return status;
                }
                return ConnectionStatus.Disconnected;
            }
        }

        public async Task<IngestionResult> IngestEventAsync(BrowserEventEnvelope envelope)
        {
            if (envelope == null)
            {
                return new IngestionResult { Accepted = false, RejectionReason = "Null envelope" };
            }

            string browser = string.IsNullOrEmpty(envelope.Browser) ? "Chrome" : envelope.Browser;
            DateTime nowUtc = DateTime.UtcNow;

            // 1. Update Connection Status & Heartbeat
            UpdateBrowserHeartbeat(browser, nowUtc);

            // 2. Handle Heartbeat and Handshake events
            if (envelope.EventType.Equals("Hello", StringComparison.OrdinalIgnoreCase) ||
                envelope.EventType.Equals("Heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                if (envelope.EventType.Equals("Hello", StringComparison.OrdinalIgnoreCase))
                {
                    _stateMachine.ResetSequence();
                }

                await _browserRepo.RegisterOrUpdateInstanceAsync(new BrowserInstanceEntity
                {
                    BrowserType = browser,
                    InstanceKey = envelope.WindowKey,
                    FirstSeenUtc = nowUtc,
                    LastSeenUtc = nowUtc,
                    ConnectionStatus = "Connected"
                });

                return new IngestionResult { Accepted = true };
            }

            // 3. Privacy Rule: Domain Exclusion Check (In-memory, before DB access)
            var rawUrl = envelope.Payload?.Url ?? string.Empty;
            var excludedPatterns = (await _settingsRepo.GetExcludedPatternsAsync()).Select(p => p.Pattern).ToList();

            if (!string.IsNullOrEmpty(rawUrl) && ExclusionPolicy.IsDomainExcluded(rawUrl, excludedPatterns))
            {
                // Excluded site: close active session if any, but do NOT persist this URL/title
                await CloseCurrentDbSessionIfExistsAsync(envelope.OccurredAtUtc, "ExcludedDomain");
                _stateMachine.ProcessEvent(envelope, out _); // Keep internal state aligned

                return new IngestionResult
                {
                    Accepted = true,
                    WasExcluded = true,
                    RejectionReason = "Domain is on user exclusion list; no browsing history persisted"
                };
            }

            // 4. Privacy Rule: URL Query String & Fragment Redaction
            bool redactionSetting = (await _settingsRepo.GetSettingAsync("UrlRedactionEnabled"))?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;
            bool wasRedacted = false;
            string effectiveUrl = rawUrl;

            if (redactionSetting && !string.IsNullOrEmpty(rawUrl))
            {
                string sanitized = UrlRedactor.RedactUrl(rawUrl, removeQueryAndFragment: true);
                if (sanitized != rawUrl)
                {
                    effectiveUrl = sanitized;
                    wasRedacted = true;
                    if (envelope.Payload != null)
                    {
                        envelope.Payload.Url = effectiveUrl;
                    }
                }
            }

            // 5. Process through Session State Machine
            int priorCompletedCount = _stateMachine.CompletedSessions.Count;
            string? rejection;
            bool accepted = _stateMachine.ProcessEvent(envelope, out rejection);
            if (!accepted)
            {
                return new IngestionResult
                {
                    Accepted = false,
                    RejectionReason = rejection
                };
            }

            // 6. Synchronize State Transitions with SQLite Persistence
            long? newSessionId = null;

            // If a previous session completed during this transition, update it in SQLite
            if (_stateMachine.CompletedSessions.Count > priorCompletedCount && _currentOpenSessionDbId > 0)
            {
                var lastCompleted = _stateMachine.CompletedSessions[_stateMachine.CompletedSessions.Count - 1];
                await _sessionRepo.UpdateSessionAsync(new BrowsingSessionEntity
                {
                    Id = _currentOpenSessionDbId,
                    EndedAtUtc = lastCompleted.EndedAtUtc,
                    ForegroundDurationMs = lastCompleted.ForegroundDurationMs,
                    EndReason = lastCompleted.EndReason,
                    PageTitle = lastCompleted.PageTitle
                });
                _currentOpenSessionDbId = 0;
            }

            // If a new session opened, insert it into SQLite
            if (_stateMachine.ActiveSession != null && _stateMachine.ActiveSession.IsActive && _currentOpenSessionDbId == 0)
            {
                var sessionEntity = new BrowsingSessionEntity
                {
                    WindowKey = _stateMachine.ActiveSession.WindowKey,
                    TabKey = _stateMachine.ActiveSession.TabKey,
                    Url = effectiveUrl,
                    NormalizedDomain = _stateMachine.ActiveSession.NormalizedDomain,
                    PageTitle = _stateMachine.ActiveSession.PageTitle,
                    StartedAtUtc = _stateMachine.ActiveSession.StartedAtUtc,
                    WasUrlRedacted = wasRedacted
                };

                newSessionId = await _sessionRepo.InsertSessionAsync(sessionEntity);
                _currentOpenSessionDbId = newSessionId.Value;

                SessionPersisted?.Invoke(sessionEntity);
            }
            else if (_currentOpenSessionDbId > 0)
            {
                // Current session continues without interruption
                newSessionId = _currentOpenSessionDbId;
            }

            return new IngestionResult
            {
                Accepted = true,
                PersistedSessionId = newSessionId,
                WasRedacted = wasRedacted
            };
        }

        public async Task SetTrackingPausedAsync(bool paused, DateTime timestampUtc)
        {
            _stateMachine.SetTrackingPaused(paused, timestampUtc);
            await _settingsRepo.SetSettingAsync("TrackingPaused", paused.ToString().ToLowerInvariant());

            if (paused)
            {
                await CloseCurrentDbSessionIfExistsAsync(timestampUtc, "Paused");
            }

            lock (_lock)
            {
                foreach (var browser in _statusPerBrowser.Keys.ToList())
                {
                    SetBrowserStatus(browser, paused ? ConnectionStatus.Paused : ConnectionStatus.Connected);
                }
            }
        }

        public async Task CheckHeartbeatTimeoutsAsync(DateTime checkTimeUtc)
        {
            List<string> timedOutBrowsers = new List<string>();

            lock (_lock)
            {
                foreach (var kvp in _lastSeenPerBrowser)
                {
                    if (_statusPerBrowser.TryGetValue(kvp.Key, out var currentStatus) && currentStatus == ConnectionStatus.Connected)
                    {
                        if (checkTimeUtc - kvp.Value > _heartbeatTimeout)
                        {
                            timedOutBrowsers.Add(kvp.Key);
                        }
                    }
                }
            }

            foreach (var browser in timedOutBrowsers)
            {
                SetBrowserStatus(browser, ConnectionStatus.Disconnected);
                await CloseCurrentDbSessionIfExistsAsync(checkTimeUtc, "DisconnectedTimeout");
                _stateMachine.HandleDisconnect(checkTimeUtc);

                var instance = await _browserRepo.GetInstanceAsync(browser, null);
                if (instance != null)
                {
                    await _browserRepo.UpdateStatusAsync(instance.Id, "Disconnected", checkTimeUtc);
                }
            }
        }

        private async Task CloseCurrentDbSessionIfExistsAsync(DateTime endedAtUtc, string reason)
        {
            if (_currentOpenSessionDbId > 0 && _stateMachine.ActiveSession != null)
            {
                DateTime start = _stateMachine.ActiveSession.StartedAtUtc;
                long dur = (long)(endedAtUtc - start).TotalMilliseconds;
                if (dur < 0) dur = 0;

                await _sessionRepo.UpdateSessionAsync(new BrowsingSessionEntity
                {
                    Id = _currentOpenSessionDbId,
                    EndedAtUtc = endedAtUtc,
                    ForegroundDurationMs = dur,
                    EndReason = reason,
                    PageTitle = _stateMachine.ActiveSession.PageTitle
                });
                _currentOpenSessionDbId = 0;
            }
        }

        private void UpdateBrowserHeartbeat(string browser, DateTime nowUtc)
        {
            lock (_lock)
            {
                _lastSeenPerBrowser[browser] = nowUtc;
                SetBrowserStatus(browser, _stateMachine.IsPaused ? ConnectionStatus.Paused : ConnectionStatus.Connected);
            }
        }

        private void SetBrowserStatus(string browser, ConnectionStatus status)
        {
            ConnectionStatus previous = ConnectionStatus.Disconnected;
            bool changed = false;

            lock (_lock)
            {
                if (!_statusPerBrowser.TryGetValue(browser, out previous) || previous != status)
                {
                    _statusPerBrowser[browser] = status;
                    changed = true;
                }
            }

            if (changed)
            {
                ConnectionStatusChanged?.Invoke(browser, status);
            }
        }
    }
}

