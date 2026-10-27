using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;
using BrowseLens.Core.Monitoring;
using BrowseLens.Core.Process;
using BrowseLens.Core.Repositories;
using BrowseLens.Core.TimeAccounting;
using BrowseLens.Desktop.ViewModels;
using BrowseLens.Infrastructure.Database;
using BrowseLens.Infrastructure.Diagnostics;
using BrowseLens.Infrastructure.Ipc;
using BrowseLens.Infrastructure.Process;
using BrowseLens.Infrastructure.Retention;

namespace BrowseLens.Desktop
{
    public class Program
    {
        private static LocalHttpEventServer _server = null!;
        private static WindowsProcessMetricsProvider _metricsProvider = null!;
        private static ProcessMetricsAggregator _aggregator = null!;
        private static ProcessSamplingScheduler _samplingScheduler = null!;
        private static SessionStateMachine _stateMachine = null!;
        private static MonitoringCoordinator _coordinator = null!;
        private static ISqliteConnectionFactory _connectionFactory = null!;
        private static IBrowsingSessionRepository _sessionRepo = null!;
        private static IProcessSampleRepository _processRepo = null!;
        private static ISettingsRepository _settingsRepo = null!;
        private static IBrowserInstanceRepository _browserRepo = null!;
        private static SessionRecoveryService _recoveryService = null!;
        private static RetentionCleanupService _retentionService = null!;
        private static LocalDiagnosticLogger _logger = null!;

        private static Timer _timeoutCheckerTimer = null!;
        private static readonly object _consoleLock = new object();

        [STAThread]
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        public static async Task MainAsync(string[] args)
        {
            _logger = new LocalDiagnosticLogger();
            _logger.LogInfo("Startup", "BrowseLens Desktop Companion starting up. Windows 11 x64, Cores: " + Environment.ProcessorCount);

            try
            {
                Console.Title = "BrowseLens Companion — Phase 4 Hardened Release";
            }
            catch { }

            Console.WriteLine("===============================================================");
            Console.WriteLine("  BrowseLens — Windows Desktop Application (Version 1.0)       ");
            Console.WriteLine("  Observability, Retention Service & Non-PII Diagnostics       ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();

            int port = 58432;
            foreach (var arg in args)
            {
                if (int.TryParse(arg, out int customPort) && customPort > 1024 && customPort < 65535)
                {
                    port = customPort;
                }
            }

            // 1. Initialize SQLite Database & Repositories
            _connectionFactory = new SqliteConnectionFactory();
            Console.WriteLine("[DB] Database file: " + _connectionFactory.DatabasePath);
            _logger.LogInfo("Database", "Database connected at: " + _connectionFactory.DatabasePath);

            var migrator = new DatabaseMigrator(_connectionFactory);
            int version = await migrator.MigrateAsync();
            Console.WriteLine("[DB] Current schema version: " + version);

            _sessionRepo = new SqliteBrowsingSessionRepository(_connectionFactory);
            _processRepo = new SqliteProcessSampleRepository(_connectionFactory);
            _settingsRepo = new SqliteSettingsRepository(_connectionFactory);
            _browserRepo = new SqliteBrowserInstanceRepository(_connectionFactory);

            // 2. Perform Session Recovery
            _recoveryService = new SessionRecoveryService(_sessionRepo);
            long recovered = await _recoveryService.RecoverOrphanedSessionsAsync(DateTime.UtcNow);
            if (recovered > 0)
            {
                Console.WriteLine("[RECOVERY] Reconciled {0} orphaned session(s) from previous unexpected termination.", recovered);
                _logger.LogInfo("Recovery", $"Reconciled {recovered} orphaned session(s) from previous unexpected termination.");
            }

            // 3. Initialize Automated Retention Background Service (FR-011)
            _retentionService = new RetentionCleanupService(_sessionRepo, _processRepo, _settingsRepo, TimeSpan.FromHours(6));
            _retentionService.RetentionExecuted += result =>
            {
                if (result.Success)
                {
                    _logger.LogInfo("Retention", $"Automated retention cleanup purged {result.DeletedSessions} sessions and {result.DeletedProcessSamples} process samples older than {result.ConfiguredRetentionDays} days.");
                }
                else
                {
                    _logger.LogWarning("Retention", $"Automated retention cleanup encountered error: {result.ErrorMessage}");
                }
            };
            _retentionService.Start();

            // 4. Initialize Domain Pipeline & Process Aggregator
            _stateMachine = new SessionStateMachine();
            _coordinator = new MonitoringCoordinator(
                _stateMachine,
                _sessionRepo,
                _settingsRepo,
                _browserRepo,
                TimeSpan.FromSeconds(45));

            _aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 15);
            _metricsProvider = new WindowsProcessMetricsProvider();
            _samplingScheduler = new ProcessSamplingScheduler(
                _metricsProvider,
                _aggregator,
                _processRepo,
                samplingInterval: TimeSpan.FromSeconds(2.0),
                dbFlushInterval: TimeSpan.FromSeconds(10.0));

            _samplingScheduler.MetricsSampled += (chromeSummary, edgeSummary) =>
            {
                lock (_consoleLock)
                {
                    PrintSummary("Chrome", chromeSummary);
                    PrintSummary("Edge", edgeSummary);
                }
            };
            _samplingScheduler.Start();

            // 5. Start Local Loopback Server
            _server = new LocalHttpEventServer(port);
            Console.WriteLine("[INFO] Environment: Windows 11 x64, Logical Cores: " + Environment.ProcessorCount);
            Console.WriteLine("[INFO] Binding Address: 127.0.0.1:" + _server.Port + " (Strict Loopback Only)");
            Console.WriteLine("[INFO] Diagnostics Log: " + _logger.LogFilePath);
            Console.WriteLine("[INFO] Authentication Token: " + _server.AuthToken);
            Console.WriteLine();
            Console.WriteLine("--> Configure this token in the Browser Extension settings.");
            Console.WriteLine("---------------------------------------------------------------");

            _server.EventReceived += async envelope =>
            {
                var result = await _coordinator.IngestEventAsync(envelope);
                lock (_consoleLock)
                {
                    if (result.Accepted)
                    {
                        if (result.WasExcluded)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.Write("[EVENT EXCLUDED] ");
                            Console.ResetColor();
                            Console.WriteLine("{0} on {1} (Excluded domain - not saved to history)", envelope.EventType, envelope.Payload?.Url);
                            _logger.LogInfo("Ingest", $"Event {envelope.EventType} dropped in-memory per user domain exclusion rule.");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("[EVENT INGESTED] ");
                            Console.ResetColor();
                            Console.WriteLine("{0} from {1} (w:{2}, t:{3}){4}",
                                envelope.EventType, envelope.Browser, envelope.WindowKey, envelope.TabKey,
                                result.WasRedacted ? " [URL REDACTED]" : "");
                            _logger.LogInfo("Ingest", $"Event {envelope.EventType} ingested from {envelope.Browser} (w:{envelope.WindowKey}, t:{envelope.TabKey}).");
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("[EVENT REJECTED] ");
                        Console.ResetColor();
                        Console.WriteLine("{0} - Reason: {1}", envelope.EventType, result.RejectionReason);
                        _logger.LogWarning("Ingest", $"Event {envelope.EventType} rejected: {result.RejectionReason}");
                    }
                }
            };

            _server.ServerLog += msg =>
            {
                Log("[IPC] " + msg);
                _logger.LogInfo("IPC", msg);
            };
            _server.Start();

            _coordinator.ConnectionStatusChanged += (browser, status) =>
            {
                Log(string.Format("[STATUS CHANGED] {0} -> {1}", browser, status));
                _logger.LogInfo("Connection", $"Browser {browser} connection state changed to: {status}");
            };

            // Background timer to check heartbeat timeouts every 10 seconds
            _timeoutCheckerTimer = new Timer(async state =>
            {
                await _coordinator.CheckHeartbeatTimeoutsAsync(DateTime.UtcNow);
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            // Determine if GUI should run or console mode
            bool consoleMode = args.Any(a => a.Equals("--console", StringComparison.OrdinalIgnoreCase)) ||
                               Console.IsInputRedirected;

            if (!consoleMode)
            {
                try
                {
                    var mainVm = new MainViewModel(
                        _coordinator,
                        _aggregator,
                        _sessionRepo,
                        _settingsRepo,
                        _processRepo,
                        _server.AuthToken,
                        _server.Port,
                        _connectionFactory.DatabasePath);

                    var app = new System.Windows.Application();
                    var window = new MainWindow(mainVm);
                    app.Run(window);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GUI] Could not launch WPF desktop window ({ex.Message}). Operating in console mode.");
                    _logger.LogWarning("GUI", $"Falling back to console mode: {ex.Message}");
                    consoleMode = true;
                }
            }

            if (consoleMode)
            {
                await RunConsoleLoopAsync();
            }

            Log("[INFO] Shutting down BrowseLens Desktop Companion...");
            _logger.LogInfo("Shutdown", "Initiating shutdown sequence...");
            await _retentionService.StopAsync();
            _retentionService.Dispose();
            await _samplingScheduler.StopAsync();
            _samplingScheduler.Dispose();
            _timeoutCheckerTimer.Dispose();
            _server.Stop();

            Log("[INFO] Shutdown complete. All state persisted to SQLite.");
            _logger.LogInfo("Shutdown", "BrowseLens shutdown complete.");
        }

        private static async Task RunConsoleLoopAsync()
        {
            Console.WriteLine("Commands: [p] Pause/Resume, [s] Status & History, [a] Rolling Aggregates, [q] Quit");
            Console.WriteLine("Waiting for browser extension events...");
            Console.WriteLine();

            bool running = true;
            while (running)
            {
                string? input = Console.ReadLine();
                if (string.IsNullOrEmpty(input)) continue;

                switch (input.Trim().ToLowerInvariant())
                {
                    case "p":
                        bool newPaused = !_stateMachine.IsPaused;
                        await _coordinator.SetTrackingPausedAsync(newPaused, DateTime.UtcNow);
                        Log("[USER] Tracking paused: " + newPaused);
                        break;
                    case "s":
                        await PrintStatusAndHistoryAsync();
                        break;
                    case "a":
                        PrintRollingAggregates();
                        break;
                    case "q":
                        running = false;
                        break;
                    default:
                        Console.WriteLine("Unknown command. Valid: [p] Pause/Resume, [s] Status, [a] Aggregates, [q] Quit");
                        break;
                }
            }
        }

        private static void PrintSummary(string name, BrowserResourceSummary s)
        {
            string cpuText = s.TotalCpuPercent.HasValue ? string.Format("{0:0.00}%", s.TotalCpuPercent.Value) : "Unavailable";
            string ramText = s.ProcessCount > 0 ? string.Format("{0:0.0} MB", s.TotalMemoryWorkingSetMb) : "0 MB";

            Console.WriteLine("   * {0,-6} : {1,2} processes | CPU: {2,7} (norm.) | RAM: {3,8} (Working Set)",
                name, s.ProcessCount, cpuText, ramText);
        }

        private static void PrintRollingAggregates()
        {
            var chromeAgg = _aggregator.GetAggregatedSummary("Chrome", 5);
            var edgeAgg = _aggregator.GetAggregatedSummary("Edge", 5);

            lock (_consoleLock)
            {
                Console.WriteLine("===============================================================");
                Console.WriteLine("  Rolling 5-Minute Resource Consumption Aggregates             ");
                Console.WriteLine("===============================================================");
                PrintAggRow("Chrome", chromeAgg);
                PrintAggRow("Edge", edgeAgg);
                Console.WriteLine("---------------------------------------------------------------");
            }
        }

        private static void PrintAggRow(string name, AggregatedMetricSummary agg)
        {
            string avgCpu = agg.AverageCpuPercent.HasValue ? string.Format("{0:0.00}%", agg.AverageCpuPercent.Value) : "N/A";
            string peakCpu = agg.PeakCpuPercent.HasValue ? string.Format("{0:0.00}%", agg.PeakCpuPercent.Value) : "N/A";

            Console.WriteLine("   * {0,-6} | Live RAM: {1,6:0.0} MB | Peak RAM: {2,6:0.0} MB | Avg CPU: {3,6} | Peak CPU: {4,6}",
                name, agg.CurrentWorkingSetMb, agg.PeakWorkingSetMb, avgCpu, peakCpu);
        }

        private static async Task PrintStatusAndHistoryAsync()
        {
            DateTime todayStartUtc = DateTime.UtcNow.Date;
            DateTime nowUtc = DateTime.UtcNow;

            long todayTotalMs = await _sessionRepo.GetTotalForegroundDurationMsAsync(todayStartUtc, nowUtc);
            var topDomains = await _sessionRepo.GetTopDomainsAsync(todayStartUtc, nowUtc, 5);
            var recentSessions = await _sessionRepo.GetSessionsAsync(todayStartUtc.AddDays(-7), nowUtc);

            lock (_consoleLock)
            {
                Console.WriteLine("---------------------------------------------------------------");
                Console.WriteLine("Chrome Status    : {0}", _coordinator.GetConnectionStatus("Chrome"));
                Console.WriteLine("Edge Status      : {0}", _coordinator.GetConnectionStatus("Edge"));
                Console.WriteLine("Tracking Paused  : {0}", _stateMachine.IsPaused);
                Console.WriteLine("Active Tab URL   : {0}", _stateMachine.ActiveSession != null ? _stateMachine.ActiveSession.Url : "None");
                Console.WriteLine("Foreground Today : {0:0.0} seconds ({1} ms)", todayTotalMs / 1000.0, todayTotalMs);
                Console.WriteLine();
                Console.WriteLine("Top Domains Today ({0}):", topDomains.Count);
                foreach (var d in topDomains)
                {
                    Console.WriteLine("   * {0,-25} : {1,5:0.0}s ({2} visits)", d.Domain, d.TotalForegroundDurationMs / 1000.0, d.VisitCount);
                }
                Console.WriteLine();
                Console.WriteLine("Recent Persisted Sessions in SQLite (Last 5 of {0}):", recentSessions.Count);
                int count = 0;
                foreach (var s in recentSessions)
                {
                    if (count++ >= 5) break;
                    Console.WriteLine("   - [{0:HH:mm:ss} - {1:HH:mm:ss}] {2,-18} ({3,5} ms) Reason: {4}",
                        s.StartedAtUtc, s.EndedAtUtc, s.NormalizedDomain, s.ForegroundDurationMs, s.EndReason);
                }
                Console.WriteLine("---------------------------------------------------------------");
            }
        }

        private static void Log(string msg)
        {
            lock (_consoleLock)
            {
                Console.WriteLine(msg);
            }
        }
    }
}
