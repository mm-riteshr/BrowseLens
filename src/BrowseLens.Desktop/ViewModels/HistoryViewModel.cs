using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Desktop.ViewModels
{
    public class BrowsingSessionItem
    {
        public long Id { get; set; }
        public string Browser { get; set; } = "Chrome";
        public string Domain { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime StartedLocal { get; set; }
        public DateTime? EndedLocal { get; set; }
        public string StartedTimeString => StartedLocal.ToString("yyyy-MM-dd HH:mm:ss");
        public string EndedTimeString => EndedLocal.HasValue ? EndedLocal.Value.ToString("HH:mm:ss") : "(Active)";
        public long DurationMs { get; set; }
        public string FormattedDuration { get; set; } = string.Empty;
        public string EndReason { get; set; } = string.Empty;
        public bool WasRedacted { get; set; }
    }

    public class HistoryViewModel : ViewModelBase
    {
        private readonly IBrowsingSessionRepository _sessionRepo;

        private string _searchText = string.Empty;
        private string _selectedDateFilter = "Today";
        private string _summaryText = "Loading history...";
        private bool _isLoading;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterAndDisplaySessions();
                }
            }
        }

        public string SelectedDateFilter
        {
            get => _selectedDateFilter;
            set
            {
                if (SetProperty(ref _selectedDateFilter, value))
                {
                    _ = LoadSessionsAsync();
                }
            }
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

        public ObservableCollection<BrowsingSessionItem> Sessions { get; } = new ObservableCollection<BrowsingSessionItem>();

        private System.Collections.Generic.List<BrowsingSessionEntity> _allFetched = new System.Collections.Generic.List<BrowsingSessionEntity>();

        public ICommand RefreshCommand { get; }
        public ICommand DeleteSessionCommand { get; }

        public HistoryViewModel(IBrowsingSessionRepository sessionRepo)
        {
            _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));

            RefreshCommand = new RelayCommand(async () => await LoadSessionsAsync());
            DeleteSessionCommand = new RelayCommand(async param =>
            {
                if (param is long id)
                {
                    await DeleteSingleSessionAsync(id);
                }
                else if (param is BrowsingSessionItem item)
                {
                    await DeleteSingleSessionAsync(item.Id);
                }
            });
        }

        public async Task LoadSessionsAsync()
        {
            IsLoading = true;
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                DateTime startUtc = SelectedDateFilter switch
                {
                    "Today" => DateTime.UtcNow.Date,
                    "Last 7 Days" => DateTime.UtcNow.Date.AddDays(-7),
                    "Last 30 Days" => DateTime.UtcNow.Date.AddDays(-30),
                    _ => DateTime.UtcNow.Date.AddYears(-5) // All History
                };

                var list = await _sessionRepo.GetSessionsAsync(startUtc, nowUtc.AddMinutes(5));
                _allFetched = list.OrderByDescending(s => s.StartedAtUtc).ToList();
                FilterAndDisplaySessions();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void FilterAndDisplaySessions()
        {
            var query = _allFetched.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string s = SearchText.Trim();
                query = query.Where(item =>
                    (item.NormalizedDomain != null && item.NormalizedDomain.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (item.PageTitle != null && item.PageTitle.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (item.Url != null && item.Url.Contains(s, StringComparison.OrdinalIgnoreCase)));
            }

            var filtered = query.ToList();

            Sessions.Clear();
            long totalMs = 0;
            foreach (var entity in filtered)
            {
                long dur = entity.ForegroundDurationMs;
                totalMs += dur;
                Sessions.Add(new BrowsingSessionItem
                {
                    Id = entity.Id,
                    Domain = entity.NormalizedDomain,
                    Title = string.IsNullOrWhiteSpace(entity.PageTitle) ? "(No Title)" : entity.PageTitle,
                    Url = entity.Url,
                    StartedLocal = entity.StartedAtUtc.ToLocalTime(),
                    EndedLocal = entity.EndedAtUtc?.ToLocalTime(),
                    DurationMs = dur,
                    FormattedDuration = FormatDuration(dur),
                    EndReason = string.IsNullOrWhiteSpace(entity.EndReason) ? (entity.IsOpen ? "In Progress" : "Closed") : entity.EndReason,
                    WasRedacted = entity.WasUrlRedacted
                });
            }

            SummaryText = string.Format("{0} session(s) | Total Foreground Time: {1}",
                filtered.Count, FormatDuration(totalMs));
        }

        public async Task DeleteSingleSessionAsync(long id)
        {
            await _sessionRepo.DeleteSessionsAsync(id: id);
            _allFetched.RemoveAll(s => s.Id == id);
            FilterAndDisplaySessions();
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
