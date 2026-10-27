using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;
using BrowseLens.Core.Monitoring;
using BrowseLens.Core.Process;
using BrowseLens.Core.Repositories;
using BrowseLens.Core.TimeAccounting;
using BrowseLens.Desktop.ViewModels;

namespace BrowseLens.UnitTests
{
    public class MockBrowsingSessionRepository : IBrowsingSessionRepository
    {
        public List<BrowsingSessionEntity> Sessions = new List<BrowsingSessionEntity>();

        public Task<long> InsertSessionAsync(BrowsingSessionEntity session)
        {
            session.Id = Sessions.Count + 1;
            Sessions.Add(session);
            return Task.FromResult(session.Id);
        }

        public Task UpdateSessionAsync(BrowsingSessionEntity session)
        {
            var idx = Sessions.FindIndex(s => s.Id == session.Id);
            if (idx >= 0) Sessions[idx] = session;
            return Task.CompletedTask;
        }

        public Task<BrowsingSessionEntity?> GetSessionByIdAsync(long id)
        {
            return Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id));
        }

        public Task<BrowsingSessionEntity?> GetActiveSessionAsync(string windowKey)
        {
            return Task.FromResult(Sessions.FirstOrDefault(s => s.WindowKey == windowKey && s.EndedAtUtc == null));
        }

        public Task<IReadOnlyList<BrowsingSessionEntity>> GetSessionsAsync(DateTime fromUtc, DateTime toUtc, string? domain = null)
        {
            var query = Sessions.Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc <= toUtc);
            if (!string.IsNullOrEmpty(domain)) query = query.Where(s => s.NormalizedDomain == domain);
            return Task.FromResult<IReadOnlyList<BrowsingSessionEntity>>(query.ToList());
        }

        public Task<long> CloseOrphanedSessionsAsync(DateTime recoveryCutoffUtc, string reason)
        {
            long count = 0;
            foreach (var s in Sessions.Where(s => s.EndedAtUtc == null))
            {
                s.EndedAtUtc = recoveryCutoffUtc;
                s.EndReason = reason;
                count++;
            }
            return Task.FromResult(count);
        }

        public Task<int> DeleteSessionsAsync(DateTime? beforeUtc = null, long? id = null)
        {
            int count = 0;
            if (id.HasValue)
            {
                count = Sessions.RemoveAll(s => s.Id == id.Value);
            }
            else if (beforeUtc.HasValue)
            {
                count = Sessions.RemoveAll(s => s.StartedAtUtc < beforeUtc.Value);
            }
            else
            {
                count = Sessions.Count;
                Sessions.Clear();
            }
            return Task.FromResult(count);
        }

        public Task<long> GetTotalForegroundDurationMsAsync(DateTime fromUtc, DateTime toUtc)
        {
            long total = Sessions.Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc <= toUtc)
                                 .Sum(s => s.ForegroundDurationMs);
            return Task.FromResult(total);
        }

        public Task<IReadOnlyList<DomainUsageSummary>> GetTopDomainsAsync(DateTime fromUtc, DateTime toUtc, int limit = 10)
        {
            var list = Sessions.Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc <= toUtc)
                               .GroupBy(s => s.NormalizedDomain)
                               .Select(g => new DomainUsageSummary
                               {
                                   Domain = g.Key,
                                   TotalForegroundDurationMs = g.Sum(s => s.ForegroundDurationMs),
                                   VisitCount = g.Count()
                               })
                               .OrderByDescending(d => d.TotalForegroundDurationMs)
                               .Take(limit)
                               .ToList();
            return Task.FromResult<IReadOnlyList<DomainUsageSummary>>(list);
        }
    }

    public class MockSettingsRepository : ISettingsRepository
    {
        public Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<ExcludedPatternEntity> Exclusions = new List<ExcludedPatternEntity>();

        public Task<string?> GetSettingAsync(string key)
        {
            Settings.TryGetValue(key, out var val);
            return Task.FromResult(val);
        }

        public Task SetSettingAsync(string key, string value)
        {
            Settings[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExcludedPatternEntity>> GetExcludedPatternsAsync()
        {
            return Task.FromResult<IReadOnlyList<ExcludedPatternEntity>>(Exclusions.ToList());
        }

        public Task AddExcludedPatternAsync(string pattern)
        {
            Exclusions.Add(new ExcludedPatternEntity { Id = Exclusions.Count + 1, Pattern = pattern.ToLowerInvariant(), CreatedAtUtc = DateTime.UtcNow });
            return Task.CompletedTask;
        }

        public Task RemoveExcludedPatternAsync(string pattern)
        {
            Exclusions.RemoveAll(p => p.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }
    }

    public class MockBrowserInstanceRepository : IBrowserInstanceRepository
    {
        public List<BrowserInstanceEntity> Instances = new List<BrowserInstanceEntity>();

        public Task<long> RegisterOrUpdateInstanceAsync(BrowserInstanceEntity instance)
        {
            instance.Id = Instances.Count + 1;
            Instances.Add(instance);
            return Task.FromResult(instance.Id);
        }

        public Task<BrowserInstanceEntity?> GetInstanceAsync(string browserType, string? instanceKey)
        {
            return Task.FromResult(Instances.FirstOrDefault(i => i.BrowserType == browserType));
        }

        public Task UpdateStatusAsync(long id, string status, DateTime lastSeenUtc)
        {
            var inst = Instances.FirstOrDefault(i => i.Id == id);
            if (inst != null)
            {
                inst.ConnectionStatus = status;
                inst.LastSeenUtc = lastSeenUtc;
            }
            return Task.CompletedTask;
        }
    }

    public class MockProcessSampleRepository : IProcessSampleRepository
    {
        public List<ProcessSampleEntity> Samples = new List<ProcessSampleEntity>();

        public Task InsertSampleAsync(ProcessSampleEntity sample)
        {
            Samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task InsertSamplesBatchAsync(IEnumerable<ProcessSampleEntity> samples)
        {
            Samples.AddRange(samples);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProcessSampleEntity>> GetRecentSamplesAsync(string browserType, int count = 50)
        {
            return Task.FromResult<IReadOnlyList<ProcessSampleEntity>>(Samples.Where(s => s.BrowserType == browserType).Take(count).ToList());
        }

        public Task<int> PurgeExpiredSamplesAsync(DateTime cutoffUtc)
        {
            int count = Samples.RemoveAll(s => s.SampledAtUtc < cutoffUtc);
            return Task.FromResult(count);
        }
    }

    public class ViewModelTests
    {
        public static void RunAll()
        {
            Test_DashboardViewModel_ActiveTabAndTotals().GetAwaiter().GetResult();
            Test_HistoryViewModel_SearchAndFiltering().GetAwaiter().GetResult();
            Test_TopDomainsViewModel_RankingAndPercentages().GetAwaiter().GetResult();
            Test_SettingsViewModel_ExclusionsAndRetention().GetAwaiter().GetResult();
            Test_MainViewModel_Navigation().GetAwaiter().GetResult();
        }

        private static async Task Test_DashboardViewModel_ActiveTabAndTotals()
        {
            var sessionRepo = new MockBrowsingSessionRepository();
            var settingsRepo = new MockSettingsRepository();
            var browserRepo = new MockBrowserInstanceRepository();
            var stateMachine = new SessionStateMachine();
            var coordinator = new MonitoringCoordinator(stateMachine, sessionRepo, settingsRepo, browserRepo);
            var aggregator = new ProcessMetricsAggregator(15);

            DateTime now = DateTime.UtcNow;

            // Ingest sample sessions for today
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 1,
                WindowKey = "w-1",
                TabKey = "t-1",
                Url = "https://learn.microsoft.com/csharp",
                NormalizedDomain = "learn.microsoft.com",
                PageTitle = "C# Documentation",
                StartedAtUtc = now.Date.AddHours(2),
                EndedAtUtc = now.Date.AddHours(2).AddMinutes(15),
                ForegroundDurationMs = 900000 // 15 mins
            });

            // Ingest active tab event
            await coordinator.IngestEventAsync(new BrowserEventEnvelope
            {
                SchemaVersion = 1,
                Browser = "Chrome",
                WindowKey = "w-1",
                TabKey = "t-2",
                OccurredAtUtc = now,
                EventType = "TabActivated",
                Payload = new TabEventPayload
                {
                    Url = "https://github.com/dotnet/runtime",
                    Title = ".NET Runtime Repo",
                    IsWindowFocused = true,
                    IsIncognito = false
                }
            });

            var dashboardVm = new DashboardViewModel(coordinator, aggregator, sessionRepo, "test_token", 58432);
            await dashboardVm.RefreshDashboardDataAsync();

            Assert(dashboardVm.ChromeStatus == "Connected", "Chrome should be marked Connected after event");
            Assert(dashboardVm.ActiveTabTitle == ".NET Runtime Repo", "Active tab title must match current session");
            Assert(dashboardVm.ActiveTabUrl == "https://github.com/dotnet/runtime", "Active tab URL must match");
            Assert(dashboardVm.ActiveTabState == "Focused & Active", "Active tab state should be Focused & Active");
            Assert(dashboardVm.TopDomainsToday.Count >= 1, "Expected at least 1 top domain today");
            Assert(dashboardVm.TopDomainsToday.Any(d => d.Domain == "learn.microsoft.com"), "Top domain list contains learn.microsoft.com");

            // Test Toggle Pause
            await dashboardVm.TogglePauseAsync();
            Assert(dashboardVm.IsTrackingPaused, "Tracking should now be paused");
            Assert(dashboardVm.PauseButtonText == "Resume Tracking", "Button text changes to Resume Tracking");
            Assert(dashboardVm.ChromeStatus == "Paused", "Chrome status changes to Paused");

            Console.WriteLine("  [PASS] Phase 3: DashboardViewModel active tab, totals, and pause toggle verified");
        }

        private static async Task Test_HistoryViewModel_SearchAndFiltering()
        {
            var sessionRepo = new MockBrowsingSessionRepository();
            DateTime now = DateTime.UtcNow;

            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 10,
                Url = "https://google.com/search",
                NormalizedDomain = "google.com",
                PageTitle = "Google Search",
                StartedAtUtc = now.AddMinutes(-30),
                ForegroundDurationMs = 60000
            });
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 11,
                Url = "https://github.com/microsoft/terminal",
                NormalizedDomain = "github.com",
                PageTitle = "Windows Terminal",
                StartedAtUtc = now.AddMinutes(-20),
                ForegroundDurationMs = 120000
            });
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 12,
                Url = "https://stackoverflow.com/questions",
                NormalizedDomain = "stackoverflow.com",
                PageTitle = "C# Questions",
                StartedAtUtc = now.AddDays(-2),
                ForegroundDurationMs = 45000
            });

            var historyVm = new HistoryViewModel(sessionRepo);
            await historyVm.LoadSessionsAsync();

            // Default "Today" filter should find 2 sessions (10 and 11)
            Assert(historyVm.Sessions.Count == 2, $"Expected 2 sessions today, got {historyVm.Sessions.Count}");

            // Test Search filtering
            historyVm.SearchText = "Terminal";
            Assert(historyVm.Sessions.Count == 1, "Search for 'Terminal' should yield 1 session");
            Assert(historyVm.Sessions[0].Domain == "github.com", "Result is github.com");

            // Clear search
            historyVm.SearchText = "";
            Assert(historyVm.Sessions.Count == 2, "Search cleared back to 2 sessions");

            // Test Single Session Deletion
            await historyVm.DeleteSingleSessionAsync(10);
            Assert(historyVm.Sessions.Count == 1, "After deleting session 10, 1 remains");
            Assert(!sessionRepo.Sessions.Any(s => s.Id == 10), "Session 10 deleted from repository");

            Console.WriteLine("  [PASS] Phase 3: HistoryViewModel date filtering, text search, and single deletion verified");
        }

        private static async Task Test_TopDomainsViewModel_RankingAndPercentages()
        {
            var sessionRepo = new MockBrowsingSessionRepository();
            DateTime now = DateTime.UtcNow;

            // 75% on github.com (300s), 25% on stackoverflow.com (100s)
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 1,
                NormalizedDomain = "github.com",
                StartedAtUtc = now.Date.AddHours(1),
                ForegroundDurationMs = 300000
            });
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 2,
                NormalizedDomain = "stackoverflow.com",
                StartedAtUtc = now.Date.AddHours(2),
                ForegroundDurationMs = 100000
            });

            var topVm = new TopDomainsViewModel(sessionRepo);
            await topVm.LoadAnalyticsAsync();

            Assert(topVm.TopDomains.Count == 2, "Expected 2 top domains");
            Assert(topVm.TopDomains[0].Domain == "github.com" && topVm.TopDomains[0].Rank == 1, "Rank 1 is github.com");
            Assert(Math.Abs(topVm.TopDomains[0].PercentageOfTotal - 75.0) < 0.5, "github.com is 75% of total time");
            Assert(topVm.TopDomains[1].Domain == "stackoverflow.com" && topVm.TopDomains[1].Rank == 2, "Rank 2 is stackoverflow.com");
            Assert(Math.Abs(topVm.TopDomains[1].PercentageOfTotal - 25.0) < 0.5, "stackoverflow.com is 25% of total time");

            Console.WriteLine("  [PASS] Phase 3: TopDomainsViewModel ranking and percentage calculations verified");
        }

        private static async Task Test_SettingsViewModel_ExclusionsAndRetention()
        {
            var settingsRepo = new MockSettingsRepository();
            var sessionRepo = new MockBrowsingSessionRepository();
            var processRepo = new MockProcessSampleRepository();
            var stateMachine = new SessionStateMachine();
            var browserRepo = new MockBrowserInstanceRepository();
            var coordinator = new MonitoringCoordinator(stateMachine, sessionRepo, settingsRepo, browserRepo);

            var settingsVm = new SettingsViewModel(settingsRepo, sessionRepo, processRepo, coordinator, "C:\\test\\browselens.db");
            await settingsVm.LoadSettingsAsync();

            // Test Adding Exclusion Pattern
            settingsVm.NewPatternInput = "*.internal.corp";
            await settingsVm.AddPatternAsync();

            Assert(settingsVm.ExcludedPatterns.Count == 1, "1 pattern added to VM");
            Assert(settingsRepo.Exclusions.Any(p => p.Pattern == "*.internal.corp"), "Pattern persisted to repository");

            // Test Removing Pattern
            await settingsVm.RemovePatternAsync("*.internal.corp");
            Assert(settingsVm.ExcludedPatterns.Count == 0, "Pattern removed from VM");
            Assert(settingsRepo.Exclusions.Count == 0, "Pattern removed from repository");

            // Test Retention Save
            settingsVm.RetentionDays = "45";
            await settingsVm.SaveRetentionAsync();
            Assert(settingsRepo.Settings["RetentionDays"] == "45", "Retention saved to 45 days");

            // Test Wipe All History
            sessionRepo.Sessions.Add(new BrowsingSessionEntity { Id = 1, Url = "test" });
            processRepo.Samples.Add(new ProcessSampleEntity { ProcessId = 1234 });

            await settingsVm.DeleteAllHistoryAsync();
            Assert(sessionRepo.Sessions.Count == 0, "All sessions deleted");
            Assert(processRepo.Samples.Count == 0, "All process samples deleted");

            Console.WriteLine("  [PASS] Phase 3: SettingsViewModel exclusions, retention, and database wipe verified");
        }

        private static async Task Test_MainViewModel_Navigation()
        {
            var settingsRepo = new MockSettingsRepository();
            var sessionRepo = new MockBrowsingSessionRepository();
            var processRepo = new MockProcessSampleRepository();
            var stateMachine = new SessionStateMachine();
            var browserRepo = new MockBrowserInstanceRepository();
            var coordinator = new MonitoringCoordinator(stateMachine, sessionRepo, settingsRepo, browserRepo);
            var aggregator = new ProcessMetricsAggregator(15);

            var mainVm = new MainViewModel(
                coordinator,
                aggregator,
                sessionRepo,
                settingsRepo,
                processRepo,
                "token",
                58432,
                "C:\\browselens.db");

            Assert(mainVm.CurrentViewModel == mainVm.Dashboard, "Default view is Dashboard");

            mainVm.NavigateHistoryCommand.Execute(null);
            Assert(mainVm.CurrentViewModel == mainVm.History, "Navigated to History");

            mainVm.NavigateTopDomainsCommand.Execute(null);
            Assert(mainVm.CurrentViewModel == mainVm.TopDomains, "Navigated to TopDomains");

            mainVm.NavigateSettingsCommand.Execute(null);
            Assert(mainVm.CurrentViewModel == mainVm.Settings, "Navigated to Settings");

            mainVm.NavigateDashboardCommand.Execute(null);
            Assert(mainVm.CurrentViewModel == mainVm.Dashboard, "Navigated back to Dashboard");

            Console.WriteLine("  [PASS] Phase 3: MainViewModel navigation flow verified");
            await Task.CompletedTask;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}

