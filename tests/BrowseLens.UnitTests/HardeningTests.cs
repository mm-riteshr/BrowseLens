using System;
using System.IO;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Infrastructure.Diagnostics;
using BrowseLens.Infrastructure.Retention;

namespace BrowseLens.UnitTests
{
    public class HardeningTests
    {
        public static void RunAll()
        {
            Test_RetentionCleanupService_AutomatedPurge().GetAwaiter().GetResult();
            Test_RetentionCleanupService_CustomRetentionDays().GetAwaiter().GetResult();
            Test_DiagnosticLogger_UrlAndTokenSanitization();
            Test_DiagnosticLogger_FileRotation();
        }

        private static async Task Test_RetentionCleanupService_AutomatedPurge()
        {
            var sessionRepo = new MockBrowsingSessionRepository();
            var processRepo = new MockProcessSampleRepository();
            var settingsRepo = new MockSettingsRepository();

            DateTime now = DateTime.UtcNow;

            // Session 1: 45 days old (should be purged under default 30-day retention)
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 1,
                Url = "https://old.com",
                NormalizedDomain = "old.com",
                StartedAtUtc = now.AddDays(-45),
                ForegroundDurationMs = 60000
            });

            // Session 2: 10 days old (should be kept)
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 2,
                Url = "https://recent.com",
                NormalizedDomain = "recent.com",
                StartedAtUtc = now.AddDays(-10),
                ForegroundDurationMs = 120000
            });

            // Process Sample: 40 days old (should be purged)
            processRepo.Samples.Add(new ProcessSampleEntity
            {
                ProcessId = 100,
                SampledAtUtc = now.AddDays(-40)
            });

            // Process Sample: 2 days old (should be kept)
            processRepo.Samples.Add(new ProcessSampleEntity
            {
                ProcessId = 101,
                SampledAtUtc = now.AddDays(-2)
            });

            using var service = new RetentionCleanupService(sessionRepo, processRepo, settingsRepo);
            bool eventFired = false;
            service.RetentionExecuted += res =>
            {
                eventFired = true;
                Assert(res.Success, "Retention execution marked as success");
                Assert(res.DeletedSessions == 1, $"Expected 1 deleted session, got {res.DeletedSessions}");
                Assert(res.DeletedProcessSamples == 1, $"Expected 1 deleted process sample, got {res.DeletedProcessSamples}");
            };

            var result = await service.ExecuteCleanupAsync(now);

            Assert(eventFired, "RetentionExecuted event must be invoked");
            Assert(result.Success, "Result indicates success");
            Assert(sessionRepo.Sessions.Count == 1, "Only 1 session remains in repository");
            Assert(sessionRepo.Sessions[0].NormalizedDomain == "recent.com", "Recent session preserved");
            Assert(processRepo.Samples.Count == 1, "Only 1 process sample remains in repository");
            Assert(processRepo.Samples[0].ProcessId == 101, "Recent process sample preserved");

            Console.WriteLine("  [PASS] Hardening: Automated retention service purges expired sessions & samples under default 30-day policy");
        }

        private static async Task Test_RetentionCleanupService_CustomRetentionDays()
        {
            var sessionRepo = new MockBrowsingSessionRepository();
            var processRepo = new MockProcessSampleRepository();
            var settingsRepo = new MockSettingsRepository();

            // User configured 7-day retention
            await settingsRepo.SetSettingAsync("RetentionDays", "7");

            DateTime now = DateTime.UtcNow;

            // Session 1: 10 days old (expired under 7-day policy)
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 1,
                Url = "https://ten-days-ago.com",
                StartedAtUtc = now.AddDays(-10)
            });

            // Session 2: 3 days old (kept under 7-day policy)
            sessionRepo.Sessions.Add(new BrowsingSessionEntity
            {
                Id = 2,
                Url = "https://three-days-ago.com",
                StartedAtUtc = now.AddDays(-3)
            });

            using var service = new RetentionCleanupService(sessionRepo, processRepo, settingsRepo);
            var result = await service.ExecuteCleanupAsync(now);

            Assert(result.ConfiguredRetentionDays == 7, "Configured retention days read as 7");
            Assert(result.DeletedSessions == 1, "1 session deleted under 7-day cutoff");
            Assert(sessionRepo.Sessions.Count == 1 && sessionRepo.Sessions[0].Id == 2, "Session from 3 days ago preserved");

            Console.WriteLine("  [PASS] Hardening: Retention service accurately reads and applies custom retention days setting");
        }

        private static void Test_DiagnosticLogger_UrlAndTokenSanitization()
        {
            string rawUrlMsg = "Ingested navigation event to https://internal.company.com/auth/login?session_token=secret_12345&user=john#dashboard";
            string sanitizedUrl = LocalDiagnosticLogger.SanitizeMessage(rawUrlMsg);

            Assert(!sanitizedUrl.Contains("session_token"), "Query string must be stripped from diagnostic logs");
            Assert(!sanitizedUrl.Contains("#dashboard"), "Fragment must be stripped from diagnostic logs");
            Assert(sanitizedUrl.Contains("https://internal.company.com/auth/login"), "Base URL path preserved");

            string rawTokenMsg = "Handshake received with Token: 82a9f1aedb3ff78ba1710f2ac87ab296";
            string sanitizedToken = LocalDiagnosticLogger.SanitizeMessage(rawTokenMsg);

            Assert(!sanitizedToken.Contains("82a9f1aedb3ff78ba1710f2ac87ab296"), "Authentication token secret must be redacted");
            Assert(sanitizedToken.Contains("[REDACTED]"), "Token replaced with [REDACTED]");

            Console.WriteLine("  [PASS] Hardening: Diagnostic logger sanitizes URL queries/fragments and redacts authentication secrets");
        }

        private static void Test_DiagnosticLogger_FileRotation()
        {
            string tempLogDir = Path.Combine(Path.GetTempPath(), "browselens_log_test_" + Guid.NewGuid().ToString("N"));
            string logFile = Path.Combine(tempLogDir, "test.log");

            try
            {
                // Set small rotation limit (500 bytes) for testing
                var logger = new LocalDiagnosticLogger(logFile, maxFileSizeBytes: 500);

                // Write multiple entries exceeding 500 bytes
                for (int i = 0; i < 20; i++)
                {
                    logger.LogInfo("Test", $"Diagnostic logging cycle entry number {i} to verify size threshold rotation.");
                }

                Assert(File.Exists(logFile), "Active log file exists");
                Assert(File.Exists(logFile + ".bak"), "Rotated backup log file (.bak) created");

                Console.WriteLine("  [PASS] Hardening: Diagnostic logger rotates logs to .bak when file exceeds size limit");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempLogDir)) Directory.Delete(tempLogDir, recursive: true);
                }
                catch { }
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}

