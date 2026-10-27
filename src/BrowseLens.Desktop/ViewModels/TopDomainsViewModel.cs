using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Desktop.ViewModels
{
    public class TopDomainAnalyticsItem
    {
        public int Rank { get; set; }
        public string Domain { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public string FormattedDuration { get; set; } = string.Empty;
        public int VisitCount { get; set; }
        public double PercentageOfTotal { get; set; }
        public string PercentageString => string.Format("{0:0.0}%", PercentageOfTotal);
    }

    public class TopDomainsViewModel : ViewModelBase
    {
        private readonly IBrowsingSessionRepository _sessionRepo;

        private string _selectedPeriod = "Today";
        private string _totalPeriodTime = "0s";
        private int _totalPeriodVisits;
        private string _summaryText = "Loading analytics...";
        private bool _isLoading;

        public string SelectedPeriod
        {
            get => _selectedPeriod;
            set
            {
                if (SetProperty(ref _selectedPeriod, value))
                {
                    _ = LoadAnalyticsAsync();
                }
            }
        }

        public string TotalPeriodTime
        {
            get => _totalPeriodTime;
            set => SetProperty(ref _totalPeriodTime, value);
        }

        public int TotalPeriodVisits
        {
            get => _totalPeriodVisits;
            set => SetProperty(ref _totalPeriodVisits, value);
        }

        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ObservableCollection<TopDomainAnalyticsItem> TopDomains { get; } = new ObservableCollection<TopDomainAnalyticsItem>();

        public ICommand RefreshCommand { get; }

        public TopDomainsViewModel(IBrowsingSessionRepository sessionRepo)
        {
            _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));
            RefreshCommand = new RelayCommand(async () => await LoadAnalyticsAsync());
        }

        public async Task LoadAnalyticsAsync()
        {
            IsLoading = true;
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                DateTime startUtc = SelectedPeriod switch
                {
                    "Today" => DateTime.UtcNow.Date,
                    "Last 7 Days" => DateTime.UtcNow.Date.AddDays(-7),
                    "Last 30 Days" => DateTime.UtcNow.Date.AddDays(-30),
                    _ => DateTime.UtcNow.Date.AddYears(-5)
                };

                long totalMs = await _sessionRepo.GetTotalForegroundDurationMsAsync(startUtc, nowUtc.AddMinutes(5));
                var domainList = await _sessionRepo.GetTopDomainsAsync(startUtc, nowUtc.AddMinutes(5), limit: 50);

                int totalVisits = domainList.Sum(d => d.VisitCount);
                TotalPeriodTime = FormatDuration(totalMs);
                TotalPeriodVisits = totalVisits;

                TopDomains.Clear();
                int rank = 1;
                foreach (var d in domainList)
                {
                    double pct = totalMs > 0 ? (d.TotalForegroundDurationMs * 100.0) / totalMs : 0.0;
                    TopDomains.Add(new TopDomainAnalyticsItem
                    {
                        Rank = rank++,
                        Domain = d.Domain,
                        DurationMs = d.TotalForegroundDurationMs,
                        FormattedDuration = FormatDuration(d.TotalForegroundDurationMs),
                        VisitCount = d.VisitCount,
                        PercentageOfTotal = Math.Round(pct, 1)
                    });
                }

                SummaryText = string.Format("Found {0} domain(s) across {1} | Total Foreground Time: {2}",
                    domainList.Count, SelectedPeriod, TotalPeriodTime);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string FormatDuration(long ms)
        {
            if (ms <= 0) return "0s";
            TimeSpan ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalHours >= 1) return string.Format("{0}h {1}m {2}s", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            if (ts.TotalMinutes >= 1) return string.Format("{0}m {1}s", ts.Minutes, ts.Seconds);
            return string.Format("{0:0}s", ts.TotalSeconds);
        }
    }
}

