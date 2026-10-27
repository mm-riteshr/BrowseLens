using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Monitoring;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Desktop.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsRepository _settingsRepo;
        private readonly IBrowsingSessionRepository _sessionRepo;
        private readonly IProcessSampleRepository _processRepo;
        private readonly MonitoringCoordinator _coordinator;
        private readonly string _databasePath;

        private bool _isTrackingPaused;
        private bool _urlRedactionEnabled = true;
        private string _retentionDays = "30";
        private string _newPatternInput = string.Empty;
        private string _statusMessage = string.Empty;

        public string DatabasePath => _databasePath;
        public string AuthToken { get; }
        public int ServerPort { get; }
        public string ServerEndpoint => $"http://127.0.0.1:{ServerPort}/events";

        public bool IsTrackingPaused
        {
            get => _isTrackingPaused;
            set
            {
                if (SetProperty(ref _isTrackingPaused, value))
                {
                    OnPropertyChanged(nameof(PauseStatusText));
                }
            }
        }

        public string PauseStatusText => IsTrackingPaused ? "Tracking is PAUSED" : "Tracking is ACTIVE";

        public bool UrlRedactionEnabled
        {
            get => _urlRedactionEnabled;
            set
            {
                if (SetProperty(ref _urlRedactionEnabled, value))
                {
                    _ = SaveRedactionSettingAsync(value);
                }
            }
        }

        public string RetentionDays
        {
            get => _retentionDays;
            set => SetProperty(ref _retentionDays, value);
        }

        public string NewPatternInput
        {
            get => _newPatternInput;
            set => SetProperty(ref _newPatternInput, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<ExcludedPatternEntity> ExcludedPatterns { get; } = new ObservableCollection<ExcludedPatternEntity>();

        public ICommand TogglePauseCommand { get; }
        public ICommand AddPatternCommand { get; }
        public ICommand RemovePatternCommand { get; }
        public ICommand SaveRetentionCommand { get; }
        public ICommand RunRetentionCleanupCommand { get; }
        public ICommand DeleteAllHistoryCommand { get; }
        public ICommand CopyTokenCommand { get; }

        public SettingsViewModel(
            ISettingsRepository settingsRepo,
            IBrowsingSessionRepository sessionRepo,
            IProcessSampleRepository processRepo,
            MonitoringCoordinator coordinator,
            string databasePath,
            string authToken = "",
            int serverPort = 58432)
        {
            _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
            _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));
            _processRepo = processRepo ?? throw new ArgumentNullException(nameof(processRepo));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _databasePath = databasePath;
            AuthToken = authToken ?? string.Empty;
            ServerPort = serverPort;

            CopyTokenCommand = new RelayCommand(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(AuthToken))
                    {
                        System.Windows.Clipboard.SetText(AuthToken);
                        StatusMessage = "Authentication token copied to clipboard!";
                    }
                }
                catch { }
            });

            TogglePauseCommand = new RelayCommand(async () => await TogglePauseAsync());
            AddPatternCommand = new RelayCommand(async () => await AddPatternAsync());
            RemovePatternCommand = new RelayCommand(async param =>
            {
                if (param is string pattern)
                {
                    await RemovePatternAsync(pattern);
                }
                else if (param is ExcludedPatternEntity entity)
                {
                    await RemovePatternAsync(entity.Pattern);
                }
            });
            SaveRetentionCommand = new RelayCommand(async () => await SaveRetentionAsync());
            RunRetentionCleanupCommand = new RelayCommand(async () => await RunRetentionCleanupAsync());
            DeleteAllHistoryCommand = new RelayCommand(async () => await DeleteAllHistoryAsync());
        }

        public async Task LoadSettingsAsync()
        {
            IsTrackingPaused = _coordinator.StateMachine.IsPaused;

            string? redaction = await _settingsRepo.GetSettingAsync("UrlRedactionEnabled");
            UrlRedactionEnabled = redaction?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;

            string? retention = await _settingsRepo.GetSettingAsync("RetentionDays");
            RetentionDays = string.IsNullOrEmpty(retention) ? "30" : retention;

            await LoadExcludedPatternsAsync();
        }

        public async Task LoadExcludedPatternsAsync()
        {
            var patterns = await _settingsRepo.GetExcludedPatternsAsync();
            ExcludedPatterns.Clear();
            foreach (var p in patterns)
            {
                ExcludedPatterns.Add(p);
            }
        }

        public async Task TogglePauseAsync()
        {
            bool newPaused = !_coordinator.StateMachine.IsPaused;
            await _coordinator.SetTrackingPausedAsync(newPaused, DateTime.UtcNow);
            IsTrackingPaused = newPaused;
            StatusMessage = newPaused ? "Tracking paused. New browsing activity will not be recorded." : "Tracking resumed.";
        }

        private async Task SaveRedactionSettingAsync(bool enabled)
        {
            await _settingsRepo.SetSettingAsync("UrlRedactionEnabled", enabled.ToString().ToLowerInvariant());
            StatusMessage = enabled ? "URL Query String & Fragment Redaction ENABLED." : "URL Redaction DISABLED (Full URLs stored).";
        }

        public async Task AddPatternAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPatternInput)) return;

            string pattern = NewPatternInput.Trim();
            await _settingsRepo.AddExcludedPatternAsync(pattern);
            NewPatternInput = string.Empty;
            await LoadExcludedPatternsAsync();
            StatusMessage = $"Added domain exclusion pattern: '{pattern}'";
        }

        public async Task RemovePatternAsync(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return;

            await _settingsRepo.RemoveExcludedPatternAsync(pattern);
            await LoadExcludedPatternsAsync();
            StatusMessage = $"Removed domain exclusion pattern: '{pattern}'";
        }

        public async Task SaveRetentionAsync()
        {
            if (int.TryParse(RetentionDays, out int days) && days > 0)
            {
                await _settingsRepo.SetSettingAsync("RetentionDays", days.ToString());
                StatusMessage = $"Retention period saved: {days} days.";
            }
            else
            {
                StatusMessage = "Invalid retention days. Please enter a positive integer.";
            }
        }

        public async Task RunRetentionCleanupAsync()
        {
            if (!int.TryParse(RetentionDays, out int days) || days <= 0) days = 30;

            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-days);
            int deletedSessions = await _sessionRepo.DeleteSessionsAsync(beforeUtc: cutoffUtc);
            int deletedSamples = await _processRepo.PurgeExpiredSamplesAsync(cutoffUtc);

            StatusMessage = $"Retention cleanup complete: Purged {deletedSessions} expired session(s) and {deletedSamples} process sample(s) older than {days} days.";
        }

        public async Task DeleteAllHistoryAsync()
        {
            int deletedSessions = await _sessionRepo.DeleteSessionsAsync();
            int deletedSamples = await _processRepo.PurgeExpiredSamplesAsync(DateTime.UtcNow.AddDays(1));

            StatusMessage = $"Database wiped: Deleted all {deletedSessions} browsing session(s) and {deletedSamples} process sample(s).";
        }
    }
}

