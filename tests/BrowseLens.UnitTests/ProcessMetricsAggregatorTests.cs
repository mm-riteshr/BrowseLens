using System;
using System.Collections.Generic;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Process;

namespace BrowseLens.UnitTests
{
    public class ProcessMetricsAggregatorTests
    {
        public static void RunAll()
        {
            Test_Aggregator_EmptyState();
            Test_Aggregator_SingleCycleAggregation();
            Test_Aggregator_RollingPeakAndAverage();
            Test_Aggregator_SlidingWindowEviction();
            Test_Aggregator_BrowserTypeIsolation();
        }

        private static void Test_Aggregator_EmptyState()
        {
            var aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 15);
            var summary = aggregator.GetAggregatedSummary("Chrome", recentMinutes: 5);

            Assert(summary != null, "Summary should not be null");
            Assert(summary!.BrowserType == "Chrome", "BrowserType matches");
            Assert(summary.ProcessCount == 0, "Empty state process count should be 0");
            Assert(!summary.CurrentCpuPercent.HasValue, "Current CPU% should be null when no samples exist");
            Assert(summary.CurrentWorkingSetBytes == 0, "Current working set should be 0");
            Assert(!summary.AverageCpuPercent.HasValue, "Average CPU should be null");
            Assert(!summary.PeakCpuPercent.HasValue, "Peak CPU should be null");
            Console.WriteLine("  [PASS] Aggregator: Empty state returns safe default metrics");
        }

        private static void Test_Aggregator_SingleCycleAggregation()
        {
            var aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 15);
            DateTime now = DateTime.UtcNow;

            var samples = new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 101,
                    ProcessStartIdentity = "101_1",
                    SampledAtUtc = now,
                    CpuPercent = 4.5,
                    MemoryBytes = 100 * 1024 * 1024 // 100 MB
                },
                new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 102,
                    ProcessStartIdentity = "102_1",
                    SampledAtUtc = now,
                    CpuPercent = 3.5,
                    MemoryBytes = 150 * 1024 * 1024 // 150 MB
                }
            };

            aggregator.AddSamples(samples);
            var summary = aggregator.GetAggregatedSummary("Chrome", recentMinutes: 5);

            Assert(summary.ProcessCount == 2, "Expected 2 processes");
            Assert(summary.CurrentCpuPercent.HasValue && Math.Abs(summary.CurrentCpuPercent.Value - 8.0) < 0.01,
                string.Format("Expected CurrentCpuPercent 8.0%, got {0}", summary.CurrentCpuPercent));
            Assert(summary.CurrentWorkingSetBytes == 250 * 1024 * 1024, "Expected 250 MB Working Set");
            Assert(Math.Abs(summary.CurrentWorkingSetMb - 250.0) < 0.01, "Working set MB calculation matches");
            Assert(summary.PeakWorkingSetBytes == 250 * 1024 * 1024, "Peak Working Set matches current in single cycle");
            Assert(summary.PeakCpuPercent.HasValue && Math.Abs(summary.PeakCpuPercent.Value - 8.0) < 0.01, "Peak CPU matches single cycle sum");
            Assert(summary.AverageCpuPercent.HasValue && Math.Abs(summary.AverageCpuPercent.Value - 8.0) < 0.01, "Average CPU matches single cycle sum");

            Console.WriteLine("  [PASS] Aggregator: Single cycle correctly sums multi-process CPU and Working Set");
        }

        private static void Test_Aggregator_RollingPeakAndAverage()
        {
            var aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 15);
            DateTime t0 = DateTime.UtcNow.AddMinutes(-3);
            DateTime t1 = DateTime.UtcNow.AddMinutes(-2);
            DateTime t2 = DateTime.UtcNow.AddMinutes(-1);

            // Cycle 0: 5% CPU, 200 MB
            aggregator.AddSamples(new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 201,
                    ProcessStartIdentity = "201_1",
                    SampledAtUtc = t0,
                    CpuPercent = 5.0,
                    MemoryBytes = 200 * 1024 * 1024
                }
            });

            // Cycle 1: 15% CPU, 350 MB (Peak)
            aggregator.AddSamples(new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 201,
                    ProcessStartIdentity = "201_1",
                    SampledAtUtc = t1,
                    CpuPercent = 15.0,
                    MemoryBytes = 350 * 1024 * 1024
                }
            });

            // Cycle 2: 10% CPU, 250 MB (Latest)
            aggregator.AddSamples(new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 201,
                    ProcessStartIdentity = "201_1",
                    SampledAtUtc = t2,
                    CpuPercent = 10.0,
                    MemoryBytes = 250 * 1024 * 1024
                }
            });

            var summary = aggregator.GetAggregatedSummary("Chrome", recentMinutes: 5);

            // Latest cycle is Cycle 2
            Assert(summary.CurrentCpuPercent.HasValue && Math.Abs(summary.CurrentCpuPercent.Value - 10.0) < 0.01,
                string.Format("Expected Current CPU 10%, got {0}", summary.CurrentCpuPercent));
            Assert(summary.CurrentWorkingSetBytes == 250 * 1024 * 1024, "Expected Current Working Set 250 MB");

            // Peak cycle is Cycle 1 (15% CPU, 350 MB)
            Assert(summary.PeakCpuPercent.HasValue && Math.Abs(summary.PeakCpuPercent.Value - 15.0) < 0.01,
                string.Format("Expected Peak CPU 15%, got {0}", summary.PeakCpuPercent));
            Assert(summary.PeakWorkingSetBytes == 350 * 1024 * 1024, "Expected Peak Working Set 350 MB");

            // Average CPU across cycles: (5 + 15 + 10) / 3 = 10.0%
            Assert(summary.AverageCpuPercent.HasValue && Math.Abs(summary.AverageCpuPercent.Value - 10.0) < 0.01,
                string.Format("Expected Average CPU 10.0%, got {0}", summary.AverageCpuPercent));

            Console.WriteLine("  [PASS] Aggregator: Multi-cycle rolling peak CPU, peak RAM, and average CPU calculate accurately");
        }

        private static void Test_Aggregator_SlidingWindowEviction()
        {
            // Aggregator with 10 minute sliding window
            var aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 10);

            DateTime oldTime = DateTime.UtcNow.AddMinutes(-20); // Outside window
            DateTime newTime = DateTime.UtcNow.AddMinutes(-1);  // Inside window

            aggregator.AddSamples(new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Edge",
                    ProcessId = 301,
                    ProcessStartIdentity = "301_1",
                    SampledAtUtc = oldTime,
                    CpuPercent = 50.0,
                    MemoryBytes = 800 * 1024 * 1024
                }
            });

            // Adding new sample triggers eviction of old sample
            aggregator.AddSamples(new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Edge",
                    ProcessId = 301,
                    ProcessStartIdentity = "301_1",
                    SampledAtUtc = newTime,
                    CpuPercent = 5.0,
                    MemoryBytes = 150 * 1024 * 1024
                }
            });

            var samplesInWindow = aggregator.GetCurrentWindowSamples();
            Assert(samplesInWindow.Count == 1, string.Format("Expected 1 sample remaining in window, got {0}", samplesInWindow.Count));
            Assert(samplesInWindow[0].SampledAtUtc == newTime, "Remaining sample is the fresh one");

            var summary = aggregator.GetAggregatedSummary("Edge", recentMinutes: 10);
            Assert(summary.PeakCpuPercent.HasValue && Math.Abs(summary.PeakCpuPercent.Value - 5.0) < 0.01,
                "Expired 50% CPU peak must be evicted from aggregate window");

            Console.WriteLine("  [PASS] Aggregator: Outdated samples evicted from sliding window");
        }

        private static void Test_Aggregator_BrowserTypeIsolation()
        {
            var aggregator = new ProcessMetricsAggregator(maxWindowMinutes: 15);
            DateTime now = DateTime.UtcNow;

            aggregator.AddSamples(new List<ProcessSampleEntity>
            {
                new ProcessSampleEntity
                {
                    BrowserType = "Chrome",
                    ProcessId = 401,
                    ProcessStartIdentity = "401_1",
                    SampledAtUtc = now,
                    CpuPercent = 12.0,
                    MemoryBytes = 300 * 1024 * 1024
                },
                new ProcessSampleEntity
                {
                    BrowserType = "Edge",
                    ProcessId = 501,
                    ProcessStartIdentity = "501_1",
                    SampledAtUtc = now,
                    CpuPercent = 4.0,
                    MemoryBytes = 180 * 1024 * 1024
                }
            });

            var chromeSummary = aggregator.GetAggregatedSummary("Chrome", recentMinutes: 5);
            var edgeSummary = aggregator.GetAggregatedSummary("Edge", recentMinutes: 5);

            Assert(chromeSummary.ProcessCount == 1 && edgeSummary.ProcessCount == 1, "Both browsers tracked independently");
            Assert(chromeSummary.CurrentCpuPercent.HasValue && Math.Abs(chromeSummary.CurrentCpuPercent.Value - 12.0) < 0.01, "Chrome CPU is 12%");
            Assert(edgeSummary.CurrentCpuPercent.HasValue && Math.Abs(edgeSummary.CurrentCpuPercent.Value - 4.0) < 0.01, "Edge CPU is 4%");
            Assert(chromeSummary.CurrentWorkingSetBytes == 300 * 1024 * 1024, "Chrome RAM is 300 MB");
            Assert(edgeSummary.CurrentWorkingSetBytes == 180 * 1024 * 1024, "Edge RAM is 180 MB");

            Console.WriteLine("  [PASS] Aggregator: Browser metrics strictly isolated between Chrome and Edge");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}

