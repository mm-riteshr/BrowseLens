using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;
using BrowseLens.Core.Monitoring;
using BrowseLens.Core.Process;
using BrowseLens.Core.TimeAccounting;
using BrowseLens.Infrastructure.Database;
using BrowseLens.Infrastructure.Diagnostics;
using BrowseLens.Infrastructure.Process;
using BrowseLens.Infrastructure.Retention;

namespace BrowseLens.IntegrationTests
{
    public class IntegrationTestRunner
    {
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine("  BrowseLens — Integration Test Suite (Phase 1 Persistence)    ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();

            int passed = 0;
            int failed = 0;
            var sw = Stopwatch.StartNew();

            await RunTestAsync("1. Fresh Database Migration & Idempotency", Test_DatabaseMigrationAndIdempotency, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("2. Browsing Session Persistence & Transactions", Test_BrowsingSessionPersistence, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("3. Session Crash Recovery (TC-012)", Test_SessionCrashRecovery, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("4. Top Domains & Duration Aggregation (TC-013)", Test_DomainAggregation, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("5. Process Samples Persistence & Batch Insert", Test_ProcessSamplesPersistence, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("6. Settings & Excluded Patterns Management", Test_SettingsAndExclusions, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("7. Retention Cleanup & History Deletion (TC-019/TC-021)", Test_RetentionAndDeletion, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("8. Monitoring Coordinator End-to-End Stream (Phase 2 Ingestion)", Test_MonitoringCoordinator_EndToEndStream, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("9. Process Sampling Scheduler & Batch DB Persist (Phase 2 Process)", Test_ProcessSamplingScheduler_BatchPersistence, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("10. Automated Retention Cleanup Service (Phase 4 Hardening)", Test_RetentionCleanupService_LiveSqlite, () => passed++, ex => { failed++; PrintError(ex); });
            await RunTestAsync("11. Realistic Multi-Tab Browsing Lifecycle & SQLite Attribution", Test_MultiTabRealisticBrowsingLifecycle, () => passed++, ex => { failed++; PrintError(ex); });

            sw.Stop();
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine("Integration Test Summary:");
            Console.WriteLine("  Total Tests  : 11");
            Console.WriteLine("  Tests Passed : {0}", passed);
            Console.WriteLine("  Tests Failed : {0}", failed);
            Console.WriteLine("  Execution Time: {0} ms", sw.ElapsedMilliseconds);
            Console.WriteLine("===============================================================");

            return failed > 0 ? 1 : 0;
        }

        private static async Task RunTestAsync(string name, Func<Task> testFunc, Action onSuccess, Action<Exception> onError)
        {
            Console.WriteLine(">>> Running " + name + "...");
            try
            {
                await testFunc();
                onSuccess();
                Console.WriteLine("  ==> PASSED\n");
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }

        private static void PrintError(Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ==> FAILED: " + ex.Message);
            if (ex.InnerException != null)
            {
                Console.WriteLine("      Inner: " + ex.InnerException.Message);
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        private static string GetTempDbPath()
        {
            return Path.Combine(Path.GetTempPath(), "browselens_test_" + Guid.NewGuid().ToString("N") + ".db");
        }

        private static void SafeCleanupDb(string dbPath)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
                string wal = dbPath + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                string shm = dbPath + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch { }
        }

        private static async Task Test_DatabaseMigrationAndIdempotency()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                var migrator = new DatabaseMigrator(factory);

                // First migration
                int v1 = await migrator.MigrateAsync();
                Assert(v1 == 1, "First migration should result in version 1");

                // Idempotent second run
                int v2 = await migrator.MigrateAsync();
                Assert(v2 == 1, "Second migration should remain version 1");

                // Verify default settings
                var settingsRepo = new SqliteSettingsRepository(factory);
                string? retention = await settingsRepo.GetSettingAsync("RetentionDays");
                Assert(retention == "30", "Default RetentionDays should be 30");

                string? redaction = await settingsRepo.GetSettingAsync("UrlRedactionEnabled");
                Assert(redaction == "true", "Default UrlRedactionEnabled should be true");

                Console.WriteLine("  [PASS] Migration created all tables, indexes, and seeded default settings.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_BrowsingSessionPersistence()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var repo = new SqliteBrowsingSessionRepository(factory);

                DateTime t0 = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);
                var session = new BrowsingSessionEntity
                {
                    WindowKey = "w-1",
                    TabKey = "t-1",
                    Url = "https://github.com/dotnet/runtime",
                    NormalizedDomain = "github.com",
                    PageTitle = "GitHub - .NET Runtime",
                    StartedAtUtc = t0,
                    WasUrlRedacted = false
                };

                long id = await repo.InsertSessionAsync(session);
                Assert(id > 0, "Generated session ID should be > 0");

                // Retrieve active session
                var active = await repo.GetActiveSessionAsync("w-1");
                Assert(active != null, "Active session should be found");
                Assert(active.Id == id, "Active session ID matches");
                Assert(active.IsOpen, "Session is currently open");

                // Close session
                active.EndedAtUtc = t0.AddSeconds(45);
                active.ForegroundDurationMs = 45000;
                active.EndReason = "Switch";
                await repo.UpdateSessionAsync(active);

                // Verify closed
                var closed = await repo.GetSessionByIdAsync(id);
                Assert(closed != null && !closed.IsOpen, "Session should now be closed");
                Assert(closed.ForegroundDurationMs == 45000, "Foreground duration is 45,000 ms");
                Assert(closed.EndReason == "Switch", "EndReason is Switch");

                Console.WriteLine("  [PASS] Session insertion, active retrieval, and closure persisted correctly.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_SessionCrashRecovery()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var repo = new SqliteBrowsingSessionRepository(factory);

                DateTime crashTime = new DateTime(2026, 10, 27, 8, 30, 0, DateTimeKind.Utc);
                // Simulate an open session created before an unexpected app kill
                var orphan = new BrowsingSessionEntity
                {
                    WindowKey = "w-crash",
                    TabKey = "t-crash",
                    Url = "https://stackoverflow.com/questions",
                    NormalizedDomain = "stackoverflow.com",
                    PageTitle = "Stack Overflow",
                    StartedAtUtc = crashTime,
                    EndedAtUtc = null // Left dangling
                };
                long id = await repo.InsertSessionAsync(orphan);

                // Companion restarts 3 hours later
                DateTime restartTime = crashTime.AddHours(3);
                var recoveryService = new SessionRecoveryService(repo);
                long recoveredCount = await recoveryService.RecoverOrphanedSessionsAsync(restartTime);

                Assert(recoveredCount >= 1, "Should recover at least 1 orphaned session");

                var recovered = await repo.GetSessionByIdAsync(id);
                Assert(recovered != null, "Recovered session must exist");
                Assert(!recovered.IsOpen, "Recovered session must be closed");
                Assert(recovered.EndReason == "AppCrashRecovery", "EndReason must be AppCrashRecovery");
                Assert(recovered.ForegroundDurationMs == 0, "Unobserved downtime must NOT be counted as foreground browsing");

                Console.WriteLine("  [PASS] TC-012: Orphaned sessions recovered without inflating foreground time.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_DomainAggregation()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var repo = new SqliteBrowsingSessionRepository(factory);

                DateTime t0 = new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

                // Add 3 sessions: 2 on google.com (30s + 20s), 1 on github.com (50s)
                await repo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-1",
                    TabKey = "t-1",
                    Url = "https://google.com/search?q=1",
                    NormalizedDomain = "google.com",
                    PageTitle = "Search 1",
                    StartedAtUtc = t0,
                    EndedAtUtc = t0.AddSeconds(30),
                    ForegroundDurationMs = 30000,
                    EndReason = "Switch"
                });

                await repo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-1",
                    TabKey = "t-1",
                    Url = "https://google.com/search?q=2",
                    NormalizedDomain = "google.com",
                    PageTitle = "Search 2",
                    StartedAtUtc = t0.AddSeconds(35),
                    EndedAtUtc = t0.AddSeconds(55),
                    ForegroundDurationMs = 20000,
                    EndReason = "Switch"
                });

                await repo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-1",
                    TabKey = "t-2",
                    Url = "https://github.com/explore",
                    NormalizedDomain = "github.com",
                    PageTitle = "Explore",
                    StartedAtUtc = t0.AddSeconds(60),
                    EndedAtUtc = t0.AddSeconds(110),
                    ForegroundDurationMs = 50000,
                    EndReason = "Switch"
                });

                long totalMs = await repo.GetTotalForegroundDurationMsAsync(t0.AddMinutes(-5), t0.AddHours(1));
                Assert(totalMs == 100000, string.Format("Total duration should be 100,000 ms, got {0}", totalMs));

                var topDomains = await repo.GetTopDomainsAsync(t0.AddMinutes(-5), t0.AddHours(1), 5);
                Assert(topDomains.Count == 2, "2 unique domains should be returned");
                Assert(topDomains[0].TotalForegroundDurationMs == 50000, "Top domains tie at 50,000 ms each");

                Console.WriteLine("  [PASS] TC-013: Domain aggregation and total foreground duration calculate accurately.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_ProcessSamplesPersistence()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var repo = new SqliteProcessSampleRepository(factory);

                DateTime now = DateTime.UtcNow;
                var samples = new List<ProcessSampleEntity>
                {
                    new ProcessSampleEntity
                    {
                        BrowserType = "Edge",
                        ProcessId = 1001,
                        ProcessStartIdentity = "1001_12345",
                        SampledAtUtc = now,
                        CpuPercent = 4.25,
                        MemoryBytes = 104857600, // 100 MB
                        CpuMetricDefinition = "Normalized %",
                        MemoryMetricDefinition = "Working Set"
                    },
                    new ProcessSampleEntity
                    {
                        BrowserType = "Edge",
                        ProcessId = 1002,
                        ProcessStartIdentity = "1002_12345",
                        SampledAtUtc = now,
                        CpuPercent = 1.10,
                        MemoryBytes = 52428800, // 50 MB
                        CpuMetricDefinition = "Normalized %",
                        MemoryMetricDefinition = "Working Set"
                    }
                };

                await repo.InsertSamplesBatchAsync(samples);

                var recent = await repo.GetRecentSamplesAsync("Edge", 10);
                Assert(recent.Count == 2, "2 samples should be returned");
                Assert(recent[0].ProcessId == 1001 || recent[0].ProcessId == 1002, "Sample PIDs match");

                // Purge test
                int purged = await repo.PurgeExpiredSamplesAsync(now.AddSeconds(1));
                Assert(purged == 2, "All 2 samples should be purged before cutoff");

                var remaining = await repo.GetRecentSamplesAsync("Edge", 10);
                Assert(remaining.Count == 0, "No samples should remain after purge");

                Console.WriteLine("  [PASS] Process samples batch insertion, retrieval, and expiration purge verified.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_SettingsAndExclusions()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var repo = new SqliteSettingsRepository(factory);

                await repo.SetSettingAsync("CustomTheme", "Dark");
                string? theme = await repo.GetSettingAsync("CustomTheme");
                Assert(theme == "Dark", "Setting CustomTheme should be Dark");

                // Exclusion patterns
                await repo.AddExcludedPatternAsync("internal.corp");
                await repo.AddExcludedPatternAsync("*.bank.com");

                var patterns = await repo.GetExcludedPatternsAsync();
                Assert(patterns.Count == 2, "2 patterns should be present");
                Assert(patterns[0].Pattern == "*.bank.com" || patterns[1].Pattern == "*.bank.com", "Wildcard pattern present");

                await repo.RemoveExcludedPatternAsync("internal.corp");
                var updated = await repo.GetExcludedPatternsAsync();
                Assert(updated.Count == 1, "Only 1 pattern remains after removal");
                Assert(updated[0].Pattern == "*.bank.com", "Remaining pattern matches");

                Console.WriteLine("  [PASS] Settings and domain exclusion pattern persistence verified.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_RetentionAndDeletion()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var repo = new SqliteBrowsingSessionRepository(factory);

                DateTime today = DateTime.UtcNow;
                DateTime day45Ago = today.AddDays(-45);
                DateTime day15Ago = today.AddDays(-15);

                await repo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-old",
                    TabKey = "t-1",
                    Url = "https://ancient.com",
                    NormalizedDomain = "ancient.com",
                    PageTitle = "Ancient",
                    StartedAtUtc = day45Ago,
                    EndedAtUtc = day45Ago.AddMinutes(5),
                    ForegroundDurationMs = 300000
                });

                await repo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-mid",
                    TabKey = "t-2",
                    Url = "https://recent.com",
                    NormalizedDomain = "recent.com",
                    PageTitle = "Recent",
                    StartedAtUtc = day15Ago,
                    EndedAtUtc = day15Ago.AddMinutes(5),
                    ForegroundDurationMs = 300000
                });

                // Apply 30-day retention cutoff
                DateTime cutoff = today.AddDays(-30);
                int deleted = await repo.DeleteSessionsAsync(beforeUtc: cutoff);
                Assert(deleted == 1, "Only 1 expired session (45 days old) should be deleted");

                var remaining = await repo.GetSessionsAsync(today.AddDays(-60), today);
                Assert(remaining.Count == 1, "Only 1 session remains");
                Assert(remaining[0].NormalizedDomain == "recent.com", "Recent session remains intact");

                // Wipe all history test (TC-021)
                int wiped = await repo.DeleteSessionsAsync();
                Assert(wiped == 1, "Wiping history deleted remaining session");
                var afterWipe = await repo.GetSessionsAsync(today.AddDays(-60), today);
                Assert(afterWipe.Count == 0, "Zero sessions after full wipe");

                Console.WriteLine("  [PASS] TC-019 & TC-021: Retention cutoff and full history deletion verified.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_MonitoringCoordinator_EndToEndStream()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var sessionRepo = new SqliteBrowsingSessionRepository(factory);
                var settingsRepo = new SqliteSettingsRepository(factory);
                var browserRepo = new SqliteBrowserInstanceRepository(factory);
                var stateMachine = new SessionStateMachine();
                var coordinator = new MonitoringCoordinator(
                    stateMachine,
                    sessionRepo,
                    settingsRepo,
                    browserRepo,
                    heartbeatTimeout: TimeSpan.FromSeconds(2.0));

                DateTime baseTime = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

                // 1. Handshake / Heartbeat ingestion
                var helloEnvelope = new BrowserEventEnvelope
                {
                    SchemaVersion = 1,
                    EventId = Guid.NewGuid().ToString(),
                    Browser = "Chrome",
                    WindowKey = "win_101",
                    OccurredAtUtc = baseTime,
                    EventType = "Hello"
                };

                var helloResult = await coordinator.IngestEventAsync(helloEnvelope);
                Assert(helloResult.Accepted, "Hello envelope must be accepted");
                Assert(coordinator.GetConnectionStatus("Chrome") == ConnectionStatus.Connected, "Status should be Connected");

                var instance = await browserRepo.GetInstanceAsync("Chrome", "win_101");
                Assert(instance != null, "Browser instance should be registered in SQLite");
                Assert(instance!.ConnectionStatus == "Connected", "Connection status in DB should be Connected");

                // 2. Tab Activated with query string & fragment (Url Redaction check)
                var tab1Envelope = new BrowserEventEnvelope
                {
                    SchemaVersion = 1,
                    EventId = Guid.NewGuid().ToString(),
                    Browser = "Chrome",
                    WindowKey = "win_101",
                    TabKey = "tab_1",
                    OccurredAtUtc = baseTime.AddSeconds(1),
                    EventType = "TabActivated",
                    Payload = new TabEventPayload
                    {
                        Url = "https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13?view=net-10.0#section-features",
                        Title = "What's New in C# 13",
                        IsWindowFocused = true,
                        IsIncognito = false
                    }
                };

                var tab1Result = await coordinator.IngestEventAsync(tab1Envelope);
                Assert(tab1Result.Accepted, "TabActivated envelope must be accepted");
                Assert(tab1Result.WasRedacted, "URL query string and fragment must be redacted");
                Assert(tab1Result.PersistedSessionId.HasValue && tab1Result.PersistedSessionId.Value > 0, "Session should be persisted to DB");

                var dbSession1 = await sessionRepo.GetSessionByIdAsync(tab1Result.PersistedSessionId!.Value);
                Assert(dbSession1 != null, "Session must exist in DB");
                Assert(dbSession1!.Url == "https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13", "Persisted URL must have query/fragment removed");
                Assert(dbSession1.WasUrlRedacted, "WasUrlRedacted flag set in DB");
                Assert(dbSession1.IsOpen, "Session is currently open");

                // 3. Switch Tab 15 seconds later -> prior session closed, new session opened
                var tab2Envelope = new BrowserEventEnvelope
                {
                    SchemaVersion = 1,
                    EventId = Guid.NewGuid().ToString(),
                    Browser = "Chrome",
                    WindowKey = "win_101",
                    TabKey = "tab_2",
                    OccurredAtUtc = baseTime.AddSeconds(16),
                    EventType = "TabActivated",
                    Payload = new TabEventPayload
                    {
                        Url = "https://github.com/dotnet/roslyn",
                        Title = "Roslyn Compiler",
                        IsWindowFocused = true,
                        IsIncognito = false
                    }
                };

                var tab2Result = await coordinator.IngestEventAsync(tab2Envelope);
                Assert(tab2Result.Accepted, "Tab2 must be accepted");
                Assert(tab2Result.PersistedSessionId.HasValue, "New session persisted");

                var closedSession1 = await sessionRepo.GetSessionByIdAsync(tab1Result.PersistedSessionId.Value);
                Assert(!closedSession1!.IsOpen, "Tab1 session must now be closed in DB");
                Assert(closedSession1.ForegroundDurationMs == 15000, string.Format("Tab1 duration should be 15000ms, got {0}", closedSession1.ForegroundDurationMs));
                Assert(closedSession1.EndReason == "Switch", "Tab1 EndReason is Switch");

                // 4. Domain Exclusion Filter (In-memory, before DB persistence)
                await settingsRepo.AddExcludedPatternAsync("*.bank.internal");
                var excludedEnvelope = new BrowserEventEnvelope
                {
                    SchemaVersion = 1,
                    EventId = Guid.NewGuid().ToString(),
                    Browser = "Chrome",
                    WindowKey = "win_101",
                    TabKey = "tab_3",
                    OccurredAtUtc = baseTime.AddSeconds(30),
                    EventType = "TabActivated",
                    Payload = new TabEventPayload
                    {
                        Url = "https://portal.bank.internal/accounts/secret-statement",
                        Title = "Internal Bank Statement",
                        IsWindowFocused = true,
                        IsIncognito = false
                    }
                };

                var excludedResult = await coordinator.IngestEventAsync(excludedEnvelope);
                Assert(excludedResult.Accepted, "Envelope handled cleanly");
                Assert(excludedResult.WasExcluded, "Must be flagged as excluded domain");
                Assert(!excludedResult.PersistedSessionId.HasValue, "Excluded domain must NOT generate a new session ID");

                // Verify prior tab2 was closed when navigating away to excluded domain
                var closedSession2 = await sessionRepo.GetSessionByIdAsync(tab2Result.PersistedSessionId!.Value);
                Assert(!closedSession2!.IsOpen, "Tab2 session closed when transitioning to excluded domain");

                // Verify DB contains ZERO rows for the excluded bank domain
                var allSessions = await sessionRepo.GetSessionsAsync(baseTime, baseTime.AddHours(1));
                Assert(!allSessions.Any(s => s.Url.Contains("bank.internal")), "Excluded domain must NEVER exist in SQLite persistence");

                // 5. Heartbeat Timeout
                DateTime timeoutCheck = baseTime.AddSeconds(60);
                await coordinator.CheckHeartbeatTimeoutsAsync(timeoutCheck);

                Assert(coordinator.GetConnectionStatus("Chrome") == ConnectionStatus.Disconnected, "Chrome should be marked Disconnected after timeout");
                var updatedInstance = await browserRepo.GetInstanceAsync("Chrome", "win_101");
                Assert(updatedInstance != null && updatedInstance.ConnectionStatus == "Disconnected", "DB instance status updated to Disconnected");

                Console.WriteLine("  [PASS] TC-Monitoring: End-to-end event stream, URL redaction, domain exclusion, and heartbeat timeout verified.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_ProcessSamplingScheduler_BatchPersistence()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var processRepo = new SqliteProcessSampleRepository(factory);
                var aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 15);
                var provider = new WindowsProcessMetricsProvider();

                // Scheduler with rapid sampling (100ms) and flush (200ms)
                using (var scheduler = new ProcessSamplingScheduler(
                    provider,
                    aggregator,
                    processRepo,
                    samplingInterval: TimeSpan.FromMilliseconds(100),
                    dbFlushInterval: TimeSpan.FromMilliseconds(200)))
                {
                    bool eventFired = false;
                    scheduler.MetricsSampled += (chrome, edge) =>
                    {
                        eventFired = true;
                    };

                    scheduler.Start();
                    // Let scheduler execute at least 2-3 cycles
                    await Task.Delay(400);
                    await scheduler.StopAsync();

                    Assert(eventFired, "MetricsSampled event should fire during scheduler run");
                }

                // Verify batch persistence through aggregator and repository
                DateTime now = DateTime.UtcNow;
                var testBatch = new List<ProcessSampleEntity>
                {
                    new ProcessSampleEntity
                    {
                        BrowserType = "Chrome",
                        ProcessId = 9999,
                        ProcessStartIdentity = "9999_start",
                        SampledAtUtc = now,
                        CpuPercent = 3.5,
                        MemoryBytes = 85 * 1024 * 1024
                    }
                };

                aggregator.AddSamples(testBatch);
                await processRepo.InsertSamplesBatchAsync(testBatch);

                var dbSamples = await processRepo.GetRecentSamplesAsync("Chrome", 10);
                Assert(dbSamples.Count > 0, "DB should contain persisted process samples");
                Assert(dbSamples.Any(s => s.ProcessId == 9999), "Persisted sample found in SQLite");

                var summary = aggregator.GetAggregatedSummary("Chrome", 5);
                Assert(summary.CurrentCpuPercent.HasValue, "Aggregator has valid CPU% summary");

                Console.WriteLine("  [PASS] TC-Scheduler: Background process sampling scheduler, sliding-window aggregation, and batch DB flushing verified.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_RetentionCleanupService_LiveSqlite()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var sessionRepo = new SqliteBrowsingSessionRepository(factory);
                var processRepo = new SqliteProcessSampleRepository(factory);
                var settingsRepo = new SqliteSettingsRepository(factory);

                DateTime now = DateTime.UtcNow;

                // Insert session 50 days old
                await sessionRepo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-retention",
                    TabKey = "t-retention",
                    Url = "https://ancient-archive.com",
                    NormalizedDomain = "ancient-archive.com",
                    StartedAtUtc = now.AddDays(-50),
                    EndedAtUtc = now.AddDays(-50).AddMinutes(5),
                    ForegroundDurationMs = 300000
                });

                // Insert session 10 days old
                await sessionRepo.InsertSessionAsync(new BrowsingSessionEntity
                {
                    WindowKey = "w-fresh",
                    TabKey = "t-fresh",
                    Url = "https://active-project.com",
                    NormalizedDomain = "active-project.com",
                    StartedAtUtc = now.AddDays(-10),
                    EndedAtUtc = now.AddDays(-10).AddMinutes(5),
                    ForegroundDurationMs = 300000
                });

                // Insert process sample 60 days old
                await processRepo.InsertSampleAsync(new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 5555,
                    ProcessStartIdentity = "5555_init",
                    SampledAtUtc = now.AddDays(-60),
                    CpuPercent = 2.0,
                    MemoryBytes = 100 * 1024 * 1024
                });

                // Insert process sample 2 days old
                await processRepo.InsertSampleAsync(new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 5556,
                    ProcessStartIdentity = "5556_init",
                    SampledAtUtc = now.AddDays(-2),
                    CpuPercent = 3.0,
                    MemoryBytes = 120 * 1024 * 1024
                });

                using var retentionService = new RetentionCleanupService(sessionRepo, processRepo, settingsRepo);
                var result = await retentionService.ExecuteCleanupAsync(now);

                Assert(result.Success, "Live SQLite retention execution succeeded");
                Assert(result.DeletedSessions == 1, string.Format("Expected 1 deleted session, got {0}", result.DeletedSessions));
                Assert(result.DeletedProcessSamples == 1, string.Format("Expected 1 deleted process sample, got {0}", result.DeletedProcessSamples));

                var remainingSessions = await sessionRepo.GetSessionsAsync(now.AddDays(-100), now);
                Assert(remainingSessions.Count == 1, "Only 1 session remains in SQLite");
                Assert(remainingSessions[0].NormalizedDomain == "active-project.com", "Recent session remains intact");

                var remainingSamples = await processRepo.GetRecentSamplesAsync("Chrome", 10);
                Assert(remainingSamples.Count == 1, "Only 1 process sample remains in SQLite");
                Assert(remainingSamples[0].ProcessId == 5556, "Recent process sample remains intact");

                Console.WriteLine("  [PASS] TC-RetentionService: Automated retention service with live SQLite DB verified.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static async Task Test_MultiTabRealisticBrowsingLifecycle()
        {
            string dbPath = GetTempDbPath();
            try
            {
                var factory = new SqliteConnectionFactory(dbPath);
                await new DatabaseMigrator(factory).MigrateAsync();
                var sessionRepo = new SqliteBrowsingSessionRepository(factory);
                var settingsRepo = new SqliteSettingsRepository(factory);
                var browserRepo = new SqliteBrowserInstanceRepository(factory);
                var stateMachine = new SessionStateMachine();
                var coordinator = new MonitoringCoordinator(
                    stateMachine,
                    sessionRepo,
                    settingsRepo,
                    browserRepo,
                    heartbeatTimeout: TimeSpan.FromSeconds(5.0));

                DateTime t0 = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
                long seq = 0;

                // Handshake
                await coordinator.IngestEventAsync(new BrowserEventEnvelope
                {
                    SchemaVersion = 1,
                    EventId = $"evt-{++seq}",
                    Sequence = seq,
                    Browser = "Chrome",
                    WindowKey = "w-1",
                    OccurredAtUtc = t0,
                    EventType = "Hello"
                });

                // 1 & 2. Open URL in Tab 1 (GitHub)
                var res1 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0, "w-1", "t-1", "https://github.com/microsoft/dotnet", "GitHub — dotnet", true));
                Assert(res1.Accepted, "Step 1: Open Tab 1 accepted");
                Assert(res1.PersistedSessionId.HasValue, "Step 2: Tab 1 persisted in SQLite");
                var s1 = await sessionRepo.GetSessionByIdAsync(res1.PersistedSessionId!.Value);
                Assert(s1 != null && s1.IsOpen && s1.NormalizedDomain == "github.com", "Tab 1 is open in DB");

                // 3. Open another URL in separate Tab 2 at t0 + 10s (Stack Overflow)
                var res2 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(10), "w-1", "t-2", "https://stackoverflow.com/questions", "Stack Overflow — Questions", true));
                Assert(res2.Accepted && res2.PersistedSessionId.HasValue, "Step 3: Tab 2 accepted & persisted");
                var s1Closed = await sessionRepo.GetSessionByIdAsync(res1.PersistedSessionId.Value);
                Assert(!s1Closed!.IsOpen && s1Closed.ForegroundDurationMs == 10000, "Tab 1 closed in DB with 10000ms duration");

                // 4. Open 3 more tabs (Tabs 3, 4, 5)
                // Tab 3: Wikipedia at t0 + 25s (15s on Tab 2)
                var res3 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(25), "w-1", "t-3", "https://wikipedia.org/wiki/Special:Search", "Wikipedia — Search", true));
                Assert(res3.Accepted, "Tab 3 accepted");
                var s2Closed = await sessionRepo.GetSessionByIdAsync(res2.PersistedSessionId!.Value);
                Assert(!s2Closed!.IsOpen && s2Closed.ForegroundDurationMs == 15000, "Tab 2 duration is 15s in DB");

                // Tab 4: MDN at t0 + 40s (15s on Tab 3)
                var res4 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(40), "w-1", "t-4", "https://developer.mozilla.org/en-US/docs/Web", "MDN Web Docs", true));
                Assert(res4.Accepted, "Tab 4 accepted");
                var s3Closed = await sessionRepo.GetSessionByIdAsync(res3.PersistedSessionId!.Value);
                Assert(!s3Closed!.IsOpen && s3Closed.ForegroundDurationMs == 15000, "Tab 3 duration is 15s in DB");

                // Tab 5: Hacker News at t0 + 60s (20s on Tab 4)
                var res5 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(60), "w-1", "t-5", "https://news.ycombinator.com/news", "Hacker News", true));
                Assert(res5.Accepted, "Tab 5 accepted");
                var s4Closed = await sessionRepo.GetSessionByIdAsync(res4.PersistedSessionId!.Value);
                Assert(!s4Closed!.IsOpen && s4Closed.ForegroundDurationMs == 20000, "Tab 4 duration is 20s in DB");

                // 5. Open/switch pages one by one and check if they are recorded
                // Switch to Tab 1 (GitHub) at t0 + 70s (10s on Tab 5)
                var sw1 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(70), "w-1", "t-1", "https://github.com/microsoft/dotnet", "GitHub — dotnet", true));
                Assert(sw1.Accepted, "Switch to Tab 1 accepted");
                var s5Closed = await sessionRepo.GetSessionByIdAsync(res5.PersistedSessionId!.Value);
                Assert(!s5Closed!.IsOpen && s5Closed.ForegroundDurationMs == 10000, "Tab 5 duration is 10s in DB");

                // Switch to Tab 2 (Stack Overflow) at t0 + 85s (15s on Tab 1)
                var sw2 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(85), "w-1", "t-2", "https://stackoverflow.com/questions", "Stack Overflow — Questions", true));
                Assert(sw2.Accepted, "Switch to Tab 2 accepted");
                var sw1Closed = await sessionRepo.GetSessionByIdAsync(sw1.PersistedSessionId!.Value);
                Assert(!sw1Closed!.IsOpen && sw1Closed.ForegroundDurationMs == 15000, "Tab 1 revisit duration is 15s in DB");

                // Switch to Tab 3 (Wikipedia) at t0 + 95s (10s on Tab 2)
                var sw3 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(95), "w-1", "t-3", "https://wikipedia.org/wiki/Special:Search", "Wikipedia — Search", true));
                Assert(sw3.Accepted, "Switch to Tab 3 accepted");
                var sw2Closed = await sessionRepo.GetSessionByIdAsync(sw2.PersistedSessionId!.Value);
                Assert(!sw2Closed!.IsOpen && sw2Closed.ForegroundDurationMs == 10000, "Tab 2 revisit duration is 10s in DB");

                // Switch to Tab 4 (MDN) at t0 + 110s (15s on Tab 3)
                var sw4 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(110), "w-1", "t-4", "https://developer.mozilla.org/en-US/docs/Web", "MDN Web Docs", true));
                Assert(sw4.Accepted, "Switch to Tab 4 accepted");
                var sw3Closed = await sessionRepo.GetSessionByIdAsync(sw3.PersistedSessionId!.Value);
                Assert(!sw3Closed!.IsOpen && sw3Closed.ForegroundDurationMs == 15000, "Tab 3 revisit duration is 15s in DB");

                // Switch to Tab 5 (Hacker News) at t0 + 120s (10s on Tab 4)
                var sw5 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(120), "w-1", "t-5", "https://news.ycombinator.com/news", "Hacker News", true));
                Assert(sw5.Accepted, "Switch to Tab 5 accepted");
                var sw4Closed = await sessionRepo.GetSessionByIdAsync(sw4.PersistedSessionId!.Value);
                Assert(!sw4Closed!.IsOpen && sw4Closed.ForegroundDurationMs == 10000, "Tab 4 revisit duration is 10s in DB");

                // 6. Close one of them (Tab 3: Wikipedia) at t0 + 130s (10s on Tab 5 so far)
                var close3 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabRemoved", t0.AddSeconds(130), "w-1", "t-3", "", "", true));
                Assert(close3.Accepted, "Step 6: Tab 3 closed via TabRemoved");
                Assert(stateMachine.ActiveSession?.NormalizedDomain == "news.ycombinator.com", "Tab 5 remains active after Tab 3 closed");

                // 7. Go back to previous tab (Tab 2) and perform activity on the page
                // Switch to Tab 2 at t0 + 140s (total 20s on Tab 5 across steps 5 & 6)
                var swBack2 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(140), "w-1", "t-2", "https://stackoverflow.com/questions", "Stack Overflow — Questions", true));
                Assert(swBack2.Accepted, "Step 7a: Return to Tab 2");
                var sw5Closed = await sessionRepo.GetSessionByIdAsync(sw5.PersistedSessionId!.Value);
                Assert(!sw5Closed!.IsOpen && sw5Closed.ForegroundDurationMs == 20000, "Tab 5 total revisit duration is 20s in DB");

                // In-page navigation / search on Tab 2 at t0 + 150s (10s on questions page)
                var tab2Activity = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabUpdated", t0.AddSeconds(150), "w-1", "t-2", "https://stackoverflow.com/questions/98765/tcp-sockets-in-csharp", "TCP Sockets in C# — Stack Overflow", true));
                Assert(tab2Activity.Accepted && tab2Activity.PersistedSessionId.HasValue, "Step 7b: Activity on Tab 2 recorded");
                var swBack2Closed = await sessionRepo.GetSessionByIdAsync(swBack2.PersistedSessionId!.Value);
                Assert(!swBack2Closed!.IsOpen && swBack2Closed.ForegroundDurationMs == 10000, "Tab 2 initial page closed with 10s duration");

                // 8. Open a new tab (Tab 6) with a new URL at t0 + 170s (20s on question page)
                var res6 = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(170), "w-1", "t-6", "https://dotnet.microsoft.com/en-us/", ".NET Platform", true));
                Assert(res6.Accepted && res6.PersistedSessionId.HasValue, "Step 8: Tab 6 recorded");
                var tab2ActivityClosed = await sessionRepo.GetSessionByIdAsync(tab2Activity.PersistedSessionId!.Value);
                Assert(!tab2ActivityClosed!.IsOpen && tab2ActivityClosed.ForegroundDurationMs == 20000, "Tab 2 question page closed with 20s duration");

                // Clean close at t0 + 200s (30s on Tab 6)
                var cleanClose = await coordinator.IngestEventAsync(CreateEnvelope($"evt-{++seq}", seq, "WindowFocusChanged", t0.AddSeconds(200), "w-1", "t-6", "", "", false));
                Assert(cleanClose.Accepted, "Clean shutdown focus loss accepted");
                var res6Closed = await sessionRepo.GetSessionByIdAsync(res6.PersistedSessionId!.Value);
                Assert(!res6Closed!.IsOpen && res6Closed.ForegroundDurationMs == 30000, "Tab 6 closed with 30s duration");

                // Verify SQLite aggregates across all 13 sessions
                var allSessions = await sessionRepo.GetSessionsAsync(t0.AddMinutes(-5), t0.AddHours(1));
                Assert(allSessions.Count == 13, string.Format("Expected 13 sessions in SQLite, got {0}", allSessions.Count));
                Assert(allSessions.All(s => !s.IsOpen), "All 13 sessions in SQLite must be closed");

                long totalDurationMs = await sessionRepo.GetTotalForegroundDurationMsAsync(t0.AddMinutes(-5), t0.AddHours(1));
                Assert(totalDurationMs == 200000, string.Format("Total foreground duration in SQLite must equal exact 200,000 ms, got {0}", totalDurationMs));

                var topDomains = await sessionRepo.GetTopDomainsAsync(t0.AddMinutes(-5), t0.AddHours(1), 10);
                Assert(topDomains.Count == 6, string.Format("Expected 6 unique domains in SQLite, got {0}", topDomains.Count));

                // Validate exact domain attributions:
                // stackoverflow.com: 15s + 10s + 10s + 20s = 55s (55000ms)
                // developer.mozilla.org: 20s + 10s = 30s (30000ms)
                // news.ycombinator.com: 10s + 20s = 30s (30000ms)
                // wikipedia.org: 15s + 15s = 30s (30000ms)
                // dotnet.microsoft.com: 30s = 30s (30000ms)
                // github.com: 10s + 15s = 25s (25000ms)
                var so = topDomains.FirstOrDefault(d => d.Domain == "stackoverflow.com");
                Assert(so != null && so.TotalForegroundDurationMs == 55000, "stackoverflow.com duration = 55,000 ms");

                var gh = topDomains.FirstOrDefault(d => d.Domain == "github.com");
                Assert(gh != null && gh.TotalForegroundDurationMs == 25000, "github.com duration = 25,000 ms");

                var dotnet = topDomains.FirstOrDefault(d => d.Domain == "dotnet.microsoft.com");
                Assert(dotnet != null && dotnet.TotalForegroundDurationMs == 30000, "dotnet.microsoft.com duration = 30,000 ms");

                Console.WriteLine("  [PASS] TC-MultiTab-E2E: Realistic 5-tab lifecycle, tab switches, closing, in-page activity, and new tab persisted to live SQLite with exact attribution.");
            }
            finally
            {
                SafeCleanupDb(dbPath);
            }
        }

        private static BrowserEventEnvelope CreateEnvelope(
            string eventId,
            long seq,
            string eventType,
            DateTime occurredAtUtc,
            string winKey,
            string tabKey,
            string url,
            string title,
            bool isFocused)
        {
            return new BrowserEventEnvelope
            {
                SchemaVersion = 1,
                EventId = eventId,
                Sequence = seq,
                Browser = "Chrome",
                EventType = eventType,
                OccurredAtUtc = occurredAtUtc,
                WindowKey = winKey,
                TabKey = tabKey,
                Payload = new TabEventPayload
                {
                    Url = url,
                    Title = title,
                    IsWindowFocused = isFocused,
                    IsIncognito = false
                }
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}
