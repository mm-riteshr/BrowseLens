using System;
using System.Threading.Tasks;
using System.Windows.Input;
using BrowseLens.Core.Monitoring;
using BrowseLens.Core.Process;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Desktop.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentViewModel;
        private string _activeSectionTitle = "Overview Dashboard";

        public DashboardViewModel Dashboard { get; }
        public HistoryViewModel History { get; }
        public TopDomainsViewModel TopDomains { get; }
        public SettingsViewModel Settings { get; }

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public string ActiveSectionTitle
        {
            get => _activeSectionTitle;
            set => SetProperty(ref _activeSectionTitle, value);
        }

        public ICommand NavigateDashboardCommand { get; }
        public ICommand NavigateHistoryCommand { get; }
        public ICommand NavigateTopDomainsCommand { get; }
        public ICommand NavigateSettingsCommand { get; }

        public MainViewModel(
            MonitoringCoordinator coordinator,
            ProcessMetricsAggregator aggregator,
            IBrowsingSessionRepository sessionRepo,
            ISettingsRepository settingsRepo,
            IProcessSampleRepository processRepo,
            string authToken,
            int serverPort,
            string databasePath)
        {
            Dashboard = new DashboardViewModel(coordinator, aggregator, sessionRepo, authToken, serverPort);
            History = new HistoryViewModel(sessionRepo);
            TopDomains = new TopDomainsViewModel(sessionRepo);
            Settings = new SettingsViewModel(settingsRepo, sessionRepo, processRepo, coordinator, databasePath, authToken, serverPort);

            _currentViewModel = Dashboard;

            NavigateDashboardCommand = new RelayCommand(async () =>
            {
                CurrentViewModel = Dashboard;
                ActiveSectionTitle = "Overview Dashboard";
                await Dashboard.RefreshDashboardDataAsync();
            });

            NavigateHistoryCommand = new RelayCommand(async () =>
            {
                CurrentViewModel = History;
                ActiveSectionTitle = "Browsing History & Inspection";
                await History.LoadSessionsAsync();
            });

            NavigateTopDomainsCommand = new RelayCommand(async () =>
            {
                CurrentViewModel = TopDomains;
                ActiveSectionTitle = "Top Domains & Website Analytics";
                await TopDomains.LoadAnalyticsAsync();
            });

            NavigateSettingsCommand = new RelayCommand(async () =>
            {
                CurrentViewModel = Settings;
                ActiveSectionTitle = "Privacy Controls & Application Settings";
                await Settings.LoadSettingsAsync();
            });
        }

        public async Task PeriodicTickAsync()
        {
            if (CurrentViewModel == Dashboard)
            {
                await Dashboard.RefreshDashboardDataAsync();
            }
        }
    }
}

