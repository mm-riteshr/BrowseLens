using System;
using System.Diagnostics;

namespace BrowseLens.UnitTests
{
    public class TestRunner
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine("  BrowseLens — Automated Unit Test Suite (Phase 0 Spike)       ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();

            int passed = 0;
            int failed = 0;
            var stopwatch = Stopwatch.StartNew();

            RunSuite("1. CPU Utilization & Scope Tests", CpuCalculationTests.RunAll, ref passed, ref failed);
            RunSuite("2. Event Envelope & Payload Validation Tests", EventValidationTests.RunAll, ref passed, ref failed);
            RunSuite("3. Session Accounting & Time Deduplication Tests", SessionAccountingTests.RunAll, ref passed, ref failed);
            RunSuite("4. Privacy Policy, Redaction & Exclusion Tests", PrivacyPolicyTests.RunAll, ref passed, ref failed);
            RunSuite("5. Sliding-Window Process Metrics Aggregator Tests", ProcessMetricsAggregatorTests.RunAll, ref passed, ref failed);
            RunSuite("6. Desktop ViewModels & Presentation Logic Tests", ViewModelTests.RunAll, ref passed, ref failed);
            RunSuite("7. Automated Retention & Diagnostics Hardening Tests", HardeningTests.RunAll, ref passed, ref failed);

            stopwatch.Stop();
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine("Test Run Summary:");
            Console.WriteLine("  Total Suites : 7");
            Console.WriteLine("  Suites Passed: {0}", passed);
            Console.WriteLine("  Suites Failed: {0}", failed);
            Console.WriteLine("  Execution Time: {0} ms", stopwatch.ElapsedMilliseconds);
            Console.WriteLine("===============================================================");

            if (failed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("RESULT: FAILED");
                Console.ResetColor();
                return 1;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("RESULT: ALL TESTS PASSED");
                Console.ResetColor();
                return 0;
            }
        }

        private static void RunSuite(string suiteName, Action action, ref int passed, ref int failed)
        {
            Console.WriteLine(">>> Running " + suiteName + "...");
            try
            {
                action();
                passed++;
                Console.WriteLine("  ==> Suite PASSED\n");
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  ==> Suite FAILED: " + ex.Message + "\n" + ex.StackTrace);
                Console.ResetColor();
                Console.WriteLine();
            }
        }
    }
}

