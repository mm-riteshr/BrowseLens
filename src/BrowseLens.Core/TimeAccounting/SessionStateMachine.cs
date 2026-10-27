using System;
using System.Collections.Generic;
using BrowseLens.Core.Models;
using BrowseLens.Core.Privacy;

namespace BrowseLens.Core.TimeAccounting
{
    public class BrowsingSessionRecord
    {
        public string WindowKey { get; set; }
        public string TabKey { get; set; }
        public string Url { get; set; }
        public string NormalizedDomain { get; set; }
        public string PageTitle { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
        public long ForegroundDurationMs { get; set; }
        public string EndReason { get; set; }
        public bool IsActive { get; set; }

        public BrowsingSessionRecord()
        {
            WindowKey = string.Empty;
            TabKey = string.Empty;
            Url = string.Empty;
            NormalizedDomain = string.Empty;
            PageTitle = string.Empty;
            StartedAtUtc = DateTime.UtcNow;
            ForegroundDurationMs = 0;
            IsActive = true;
        }
    }

    public class SessionStateMachine
    {
        private readonly object _syncLock = new object();
        private BrowsingSessionRecord _activeSession;
        private long _lastSequence = -1;
        private readonly HashSet<string> _processedEventIds = new HashSet<string>();
        private readonly List<BrowsingSessionRecord> _completedSessions = new List<BrowsingSessionRecord>();

        public bool IsPaused { get; private set; }
        public bool IsWindowFocused { get; private set; }
        public BrowsingSessionRecord ActiveSession { get { return _activeSession; } }
        public IReadOnlyList<BrowsingSessionRecord> CompletedSessions { get { return _completedSessions.AsReadOnly(); } }

        public SessionStateMachine()
        {
            IsPaused = false;
            IsWindowFocused = true;
        }

        public void SetTrackingPaused(bool paused, DateTime timestampUtc)
        {
            lock (_syncLock)
            {
                if (IsPaused == paused) return;
                IsPaused = paused;

                if (IsPaused)
                {
                    InternalCloseActiveSession(timestampUtc, "Paused");
                }
            }
        }

        public void ResetSequence()
        {
            lock (_syncLock)
            {
                _lastSequence = -1;
            }
        }

        public bool ProcessEvent(BrowserEventEnvelope envelope, out string rejectionReason)
        {
            rejectionReason = null;
            if (envelope == null)
            {
                rejectionReason = "Null envelope";
                return false;
            }

            if (envelope.Payload == null)
            {
                rejectionReason = "Missing payload";
                return false;
            }

            // Privacy check: reject incognito activity
            if (envelope.Payload.IsIncognito)
            {
                rejectionReason = "Incognito browsing is not collected";
                return false;
            }

            lock (_syncLock)
            {
                // Deduplication check
                if (!string.IsNullOrEmpty(envelope.EventId))
                {
                    if (_processedEventIds.Contains(envelope.EventId))
                    {
                        rejectionReason = "Duplicate event ID ignored";
                        return false;
                    }
                    _processedEventIds.Add(envelope.EventId);
                    if (_processedEventIds.Count > 1000)
                    {
                        // Prevent unbounded memory growth for event IDs
                        _processedEventIds.Clear();
                        _processedEventIds.Add(envelope.EventId);
                    }
                }

                // Sequence monotonic check
                if (envelope.Sequence > 0 && envelope.Sequence <= _lastSequence)
                {
                    // If sequence reset to 1, gracefully resynchronize stream from restarted extension/worker
                    if (envelope.Sequence == 1)
                    {
                        _lastSequence = 1;
                    }
                    else
                    {
                        rejectionReason = "Out-of-order event sequence rejected";
                        return false;
                    }
                }
                else if (envelope.Sequence > 0)
                {
                    _lastSequence = envelope.Sequence;
                }

                // Privacy check: restricted URLs (chrome://, edge://, about:)
                if (!string.IsNullOrEmpty(envelope.Payload.Url) && ExclusionPolicy.IsRestrictedUrl(envelope.Payload.Url))
                {
                    InternalCloseActiveSession(envelope.OccurredAtUtc, "RestrictedPage");
                    return true;
                }

                if (IsPaused)
                {
                    return true; // Tracking paused; ignore foreground time accumulation
                }

                string eventType = envelope.EventType ?? string.Empty;

                if (eventType.Equals("WindowFocusChanged", StringComparison.OrdinalIgnoreCase))
                {
                    IsWindowFocused = envelope.Payload.IsWindowFocused;
                    if (!IsWindowFocused)
                    {
                        InternalCloseActiveSession(envelope.OccurredAtUtc, "FocusLost");
                    }
                    else if (!string.IsNullOrEmpty(envelope.Payload.Url))
                    {
                        StartNewSession(envelope, "FocusRegained");
                    }
                    return true;
                }

                if (eventType.Equals("TabActivated", StringComparison.OrdinalIgnoreCase) ||
                    eventType.Equals("TabUpdated", StringComparison.OrdinalIgnoreCase))
                {
                    IsWindowFocused = envelope.Payload.IsWindowFocused;

                    // If window is not focused, don't accumulate foreground session
                    if (!IsWindowFocused)
                    {
                        InternalCloseActiveSession(envelope.OccurredAtUtc, "WindowNotFocused");
                        return true;
                    }

                    // Check if this is the identical active session (no URL change)
                    if (_activeSession != null &&
                        _activeSession.WindowKey == envelope.WindowKey &&
                        _activeSession.TabKey == envelope.TabKey &&
                        _activeSession.Url == envelope.Payload.Url)
                    {
                        // Just update title if it changed
                        if (!string.IsNullOrEmpty(envelope.Payload.Title))
                        {
                            _activeSession.PageTitle = envelope.Payload.Title;
                        }
                        return true;
                    }

                    // Active tab or URL changed: close prior and start new
                    string reason = eventType.Equals("TabActivated", StringComparison.OrdinalIgnoreCase) ? "Switch" : "Navigation";
                    InternalCloseActiveSession(envelope.OccurredAtUtc, reason);
                    StartNewSession(envelope, reason);
                    return true;
                }

                if (eventType.Equals("TabRemoved", StringComparison.OrdinalIgnoreCase))
                {
                    if (_activeSession != null &&
                        _activeSession.WindowKey == envelope.WindowKey &&
                        _activeSession.TabKey == envelope.TabKey)
                    {
                        InternalCloseActiveSession(envelope.OccurredAtUtc, "TabClosed");
                    }
                    return true;
                }

                if (eventType.Equals("BrowserStateChanged", StringComparison.OrdinalIgnoreCase))
                {
                    InternalCloseActiveSession(envelope.OccurredAtUtc, "BrowserDisconnected");
                    return true;
                }

                return true;
            }
        }

        public void HandleDisconnect(DateTime timestampUtc)
        {
            lock (_syncLock)
            {
                InternalCloseActiveSession(timestampUtc, "Disconnected");
            }
        }

        public void CloseActiveSession(DateTime endTimestampUtc, string reason = "Closed")
        {
            lock (_syncLock)
            {
                InternalCloseActiveSession(endTimestampUtc, reason);
            }
        }

        private void StartNewSession(BrowserEventEnvelope envelope, string reason)
        {
            string domain = UrlRedactor.ExtractDomain(envelope.Payload.Url);
            _activeSession = new BrowsingSessionRecord
            {
                WindowKey = envelope.WindowKey ?? "w-0",
                TabKey = envelope.TabKey ?? "t-0",
                Url = envelope.Payload.Url ?? string.Empty,
                NormalizedDomain = domain,
                PageTitle = envelope.Payload.Title ?? string.Empty,
                StartedAtUtc = envelope.OccurredAtUtc,
                IsActive = true
            };
        }

        private void InternalCloseActiveSession(DateTime endTimestampUtc, string reason)
        {
            if (_activeSession == null || !_activeSession.IsActive) return;

            DateTime end = endTimestampUtc > _activeSession.StartedAtUtc ? endTimestampUtc : _activeSession.StartedAtUtc;
            long durationMs = (long)(end - _activeSession.StartedAtUtc).TotalMilliseconds;
            if (durationMs < 0) durationMs = 0;

            _activeSession.EndedAtUtc = end;
            _activeSession.ForegroundDurationMs = durationMs;
            _activeSession.EndReason = reason;
            _activeSession.IsActive = false;

            _completedSessions.Add(_activeSession);
            _activeSession = null;
        }
    }
}

