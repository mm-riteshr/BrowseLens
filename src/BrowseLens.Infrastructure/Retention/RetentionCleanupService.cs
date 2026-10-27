using System;
using System.Threading;
using System.Threading.Tasks;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Infrastructure.Retention
{
    public class RetentionCleanupResult
    {
        public int DeletedSessions { get; set; }
        public int DeletedProcessSamples { get; set; }
        public int ConfiguredRetentionDays { get; set; }
        public DateTime CutoffUtc { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class RetentionCleanupService : IDisposable
    {
        private readonly IBrowsingSessionRepository _sessionRepo;
        private readonly IProcessSampleRepository _processRepo;
        private readonly ISettingsRepository _settingsRepo;
        private readonly TimeSpan _checkInterval;
        private Timer? _timer;
        private volatile bool _isRunning;
        private bool _disposed;

        public event Action<RetentionCleanupResult>? RetentionExecuted;

        public RetentionCleanupService(
            IBrowsingSessionRepository sessionRepo,
            IProcessSampleRepository processRepo,
            ISettingsRepository settingsRepo,
            TimeSpan? checkInterval = null)
        {
            _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));
            _processRepo = processRepo ?? throw new ArgumentNullException(nameof(processRepo));
            _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
            // Default check interval: every 6 hours
            _checkInterval = checkInterval ?? TimeSpan.FromHours(6);
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            // Run first cleanup 5 seconds after startup, then at periodic interval
            _timer = new Timer(async _ => await ExecuteScheduledCleanupAsync(), null, TimeSpan.FromSeconds(5), _checkInterval);
        }

        public async Task<RetentionCleanupResult> ExecuteCleanupAsync(DateTime nowUtc)
        {
            var result = new RetentionCleanupResult
            {
                ExecutedAtUtc = nowUtc,
                ConfiguredRetentionDays = 30
            };

            try
            {
                // 1. Fetch configured retention days from settings
                string? retentionSetting = await _settingsRepo.GetSettingAsync("RetentionDays");
                if (int.TryParse(retentionSetting, out int days) && days > 0)
                {
                    result.ConfiguredRetentionDays = days;
                }

                // 2. Compute cutoff timestamp
                DateTime cutoffUtc = nowUtc.AddDays(-result.ConfiguredRetentionDays);
                result.CutoffUtc = cutoffUtc;

                // 3. Purge expired sessions and process samples
                result.DeletedSessions = await _sessionRepo.DeleteSessionsAsync(beforeUtc: cutoffUtc);
                result.DeletedProcessSamples = await _processRepo.PurgeExpiredSamplesAsync(cutoffUtc);
                result.Success = true;

                RetentionExecuted?.Invoke(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task ExecuteScheduledCleanupAsync()
        {
            if (!_isRunning) return;

            try
            {
                await ExecuteCleanupAsync(DateTime.UtcNow);
            }
            catch
            {
                // Background exception must not crash host application
            }
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            if (_timer != null)
            {
                await _timer.DisposeAsync();
                _timer = null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _isRunning = false;
                _timer?.Dispose();
            }
        }
    }
}

