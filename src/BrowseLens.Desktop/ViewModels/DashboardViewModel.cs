using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;
using BrowseLens.Core.Monitoring;
using BrowseLens.Core.Process;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Desktop.ViewModels
{
    public class DomainSummaryItem
    {
        public string Domain { get; set; } = string.Empty;
        public string FormattedDuration { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public int VisitCount { get; set; }
    }

    public class DashboardViewModel : ViewModelBase
    {
        private readonly MonitoringCoordinator _coordinator;
        private readonly ProcessMetricsAggregator _aggregator;
        private readonly IBrowsingSessionRepository _sessionRepo;
        private readonly string _authToken;
        private readonly int _serverPort;

        private string _chromeStatus = "Disconnected";
        private string _edgeStatus = "Disconnected";
        private bool _isTrackingPaused;
        private string _activeTabTitle = "No Active Tab";
        private string _activeTabUrl = "None";
        private string _activeTabState = "Idle";
        private string _todayForegroundTime = "0 sec";
        private int _todayVisitsCount;

        // Chrome Metrics
        private int _chromeProcessCount;
        private string _chromeCurrentRam = "0 MB";
        private string _chromePeakRam = "0 MB";
        private string _chromeCurrentCpu = "N/A";
        private string _chromeAvgCpu = "N/A";
        private string _chromePeakCpu = "N/A";

        // Edge Metrics
        private int _edgeProcessCount;
        private string _edgeCurrentRam = "0 MB";
        private string _edgePeakRam = "0 MB";
        private string _edgeCurrentCpu = "N/A";
        private string _edgeAvgCpu = "N/A";
        private string _edgePeakCpu = "N/A";

        public string AuthToken => _authToken;
        public int ServerPort => _serverPort;

        public string ChromeStatus
        {
            get => _chromeStatus;
            set => SetProperty(ref _chromeStatus, value);
        }

        public string EdgeStatus
        {
            get => _edgeStatus;
            set => SetProperty(ref _edgeStatus, value);
        }

        public bool IsTrackingPaused
        {
            get => _isTrackingPaused;
            set
            {
                if (SetProperty(ref _isTrackingPaused, value))
                {
                    OnPropertyChanged(nameof(PauseButtonText));
                    OnPropertyChanged(nameof(TrackingStatusText));
                }
            }
        }

        public string PauseButtonText => IsTrackingPaused ? "Resume Tracking" : "Pause Tracking";
        public string TrackingStatusText => IsTrackingPaused ? "Tracking is PAUSED (No browsing history recorded)" : "Tracking is ACTIVE (Foreground tabs monitored)";

        public string ActiveTabTitle
        {
            get => _activeTabTitle;
            set => SetProperty(ref _activeTabTitle, value);
        }

        public string ActiveTabUrl
        {
            get => _activeTabUrl;
            set => SetProperty(ref _activeTabUrl, value);
        }

        public string ActiveTabState
        {
            get => _activeTabState;
            set => SetProperty(ref _activeTabState, value);
        }

        public string TodayForegroundTime
        {
            get => _todayForegroundTime;
            set => SetProperty(ref _todayForegroundTime, value);
        }

        public int TodayVisitsCount
        {
            get => _todayVisitsCount;
            set => SetProperty(ref _todayVisitsCount, value);
        }

        // Chrome properties
        public int ChromeProcessCount { get => _chromeProcessCount; set => SetProperty(ref _chromeProcessCount, value); }
        public string ChromeCurrentRam { get => _chromeCurrentRam; set => SetProperty(ref _chromeCurrentRam, value); }
        public string ChromePeakRam { get => _chromePeakRam; set => SetProperty(ref _chromePeakRam, value); }
        public string ChromeCurrentCpu { get => _chromeCurrentCpu; set => SetProperty(ref _chromeCurrentCpu, value); }
        public string ChromeAvgCpu { get => _chromeAvgCpu; set => SetProperty(ref _chromeAvgCpu, value); }
        public string ChromePeakCpu { get => _chromePeakCpu; set => SetProperty(ref _chromePeakCpu, value); }

        // Edge properties
        public int EdgeProcessCount { get => _edgeProcessCount; set => SetProperty(ref _edgeProcessCount, value); }
        public string EdgeCurrentRam { get => _edgeCurrentRam; set => SetProperty(ref _edgeCurrentRam, value); }
        public string EdgePeakRam { get => _edgePeakRam; set => SetProperty(ref _edgePeakRam, value); }
        public string EdgeCurrentCpu { get => _edgeCurrentCpu; set => SetProperty(ref _edgeCurrentCpu, value); }
        public string EdgeAvgCpu { get => _edgeAvgCpu; set => SetProperty(ref _edgeAvgCpu, value); }
        public string EdgePeakCpu { get => _edgePeakCpu; set => SetProperty(ref _edgePeakCpu, value); }

        public ObservableCollection<DomainSummaryItem> TopDomainsToday { get; } = new ObservableCollection<DomainSummaryItem>();

        public ICommand TogglePauseCommand { get; }
        public ICommand CopyTokenCommand { get; }

        public DashboardViewModel(
            MonitoringCoordinator coordinator,
            ProcessMetricsAggregator aggregator,
            IBrowsingSessionRepository sessionRepo,
            string authToken,
            int serverPort)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
            _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));
            _authToken = authToken;
            _serverPort = serverPort;

            TogglePauseCommand = new RelayCommand(async () => await TogglePauseAsync());
            CopyTokenCommand = new RelayCommand(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(_authToken))
                    {
                        System.Windows.Clipboard.SetText(_authToken);
                    }
                }
                catch { }
            });
        }

        public async Task TogglePauseAsync()
        {
            bool newPaused = !_coordinator.StateMachine.IsPaused;
            await _coordinator.SetTrackingPausedAsync(newPaused, DateTime.UtcNow);
            IsTrackingPaused = newPaused;
            UpdateConnectionStatuses();
        }

        public void UpdateConnectionStatuses()
        {
            ChromeStatus = _coordinator.GetConnectionStatus("Chrome").ToString();
            EdgeStatus = _coordinator.GetConnectionStatus("Edge").ToString();
            IsTrackingPaused = _coordinator.StateMachine.IsPaused;
        }

        public async Task RefreshDashboardDataAsync()
        {
            UpdateConnectionStatuses();

            // 1. Active Tab
            var active = _coordinator.StateMachine.ActiveSession;
            if (active != null && active.IsActive && !_coordinator.StateMachine.IsPaused)
            {
                ActiveTabTitle = string.IsNullOrWhiteSpace(active.PageTitle) ? "(Untitled Page)" : active.PageTitle;
                ActiveTabUrl = string.IsNullOrWhiteSpace(active.Url) ? "about:blank" : active.Url;
                ActiveTabState = "Focused & Active";
            }
            else if (_coordinator.StateMachine.IsPaused)
            {
                ActiveTabTitle = "Tracking Paused";
                ActiveTabUrl = "-";
                ActiveTabState = "Paused by User";
            }
            else
            {
                ActiveTabTitle = "No Active Tab";
                ActiveTabUrl = "-";
                ActiveTabState = "Browser window unfocused or closed";
            }

            // 2. Process Metrics (from Aggregator)
            var chromeAgg = _aggregator.GetAggregatedSummary("Chrome", 5);
            ChromeProcessCount = chromeAgg.ProcessCount;
            ChromeCurrentRam = string.Format("{0:0.0} MB", chromeAgg.CurrentWorkingSetMb);
            ChromePeakRam = string.Format("{0:0.0} MB", chromeAgg.PeakWorkingSetMb);
            ChromeCurrentCpu = chromeAgg.CurrentCpuPercent.HasValue ? string.Format("{0:0.00}%", chromeAgg.CurrentCpuPercent.Value) : "Unavailable";
            ChromeAvgCpu = chromeAgg.AverageCpuPercent.HasValue ? string.Format("{0:0.00}%", chromeAgg.AverageCpuPercent.Value) : "N/A";
            ChromePeakCpu = chromeAgg.PeakCpuPercent.HasValue ? string.Format("{0:0.00}%", chromeAgg.PeakCpuPercent.Value) : "N/A";

            var edgeAgg = _aggregator.GetAggregatedSummary("Edge", 5);
            EdgeProcessCount = edgeAgg.ProcessCount;
            EdgeCurrentRam = string.Format("{0:0.0} MB", edgeAgg.CurrentWorkingSetMb);
            EdgePeakRam = string.Format("{0:0.0} MB", edgeAgg.PeakWorkingSetMb);
            EdgeCurrentCpu = edgeAgg.CurrentCpuPercent.HasValue ? string.Format("{0:0.00}%", edgeAgg.CurrentCpuPercent.Value) : "Unavailable";
            EdgeAvgCpu = edgeAgg.AverageCpuPercent.HasValue ? string.Format("{0:0.00}%", edgeAgg.AverageCpuPercent.Value) : "N/A";
            EdgePeakCpu = edgeAgg.PeakCpuPercent.HasValue ? string.Format("{0:0.00}%", edgeAgg.PeakCpuPercent.Value) : "N/A";

            // 3. Today's Foreground Time and Top Domains
            DateTime todayUtc = DateTime.UtcNow.Date;
            DateTime nowUtc = DateTime.UtcNow;

            long totalMs = await _sessionRepo.GetTotalForegroundDurationMsAsync(todayUtc, nowUtc);
            // Add live active interval if active
            if (active != null && active.IsActive && !_coordinator.StateMachine.IsPaused)
            {
                long liveDelta = (long)(nowUtc - active.StartedAtUtc).TotalMilliseconds;
                if (liveDelta > 0) totalMs += liveDelta;
            }

            TodayForegroundTime = FormatDuration(totalMs);

            var topDomains = await _sessionRepo.GetTopDomainsAsync(todayUtc, nowUtc, 5);
            int visitsSum = 0;
            TopDomainsToday.Clear();
            foreach (var d in topDomains)
            {
                visitsSum += d.VisitCount;
                TopDomainsToday.Add(new DomainSummaryItem
                {
                    Domain = d.Domain,
                    DurationMs = d.TotalForegroundDurationMs,
                    FormattedDuration = FormatDuration(d.TotalForegroundDurationMs),
                    VisitCount = d.VisitCount
                });
            }
            TodayVisitsCount = visitsSum;
        }

        private static string FormatDuration(long ms)
        {
            if (ms <= 0) return "0 sec";
            TimeSpan ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalHours >= 1)
            {
                return string.Format("{0}h {1}m {2}s", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }
            if (ts.TotalMinutes >= 1)
            {
                return string.Format("{0}m {1}s", ts.Minutes, ts.Seconds);
            }
            return string.Format("{0:0} sec", ts.TotalSeconds);
        }
    }
}

