using System;
using BrowseLens.Core.TimeAccounting;

namespace BrowseLens.UnitTests
{
    public class CpuCalculationTests
    {
        public static void RunAll()
        {
            Test_CpuCalculation_FromSuccessiveSamples();
            Test_CpuCalculation_ProcessDisappearsOrRestarts();
            Test_CpuCalculation_Clamping();
            Test_ProcessIdentity_GuardsAgainstPidReuse();
            Test_MetricScope_Definitions();
        }

        private static void Test_CpuCalculation_FromSuccessiveSamples()
        {
            DateTime t0 = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(2.0); // 2 second wall-clock delta

            TimeSpan cpu0 = TimeSpan.FromSeconds(10.0);
            TimeSpan cpu1 = TimeSpan.FromSeconds(11.0); // 1 second total CPU delta

            int logicalCores = 4;
            // Formula: (1.0 / (2.0 * 4)) * 100 = (1.0 / 8.0) * 100 = 12.5%
            double? cpu = CpuCalculator.CalculateNormalizedCpuPercent(cpu0, cpu1, t0, t1, logicalCores);

            Assert(cpu.HasValue, "TC-001: CPU calculation should return a value");
            Assert(Math.Abs(cpu.Value - 12.5) < 0.01, string.Format("TC-001: Expected 12.5%, got {0}", cpu.Value));
            Console.WriteLine("  [PASS] TC-001: Successive CPU sample delta calculation (12.5%)");
        }

        private static void Test_CpuCalculation_ProcessDisappearsOrRestarts()
        {
            DateTime t0 = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(2.0);

            // CPU time drops (e.g. process terminated and another was spawned with lower CPU time)
            TimeSpan cpu0 = TimeSpan.FromSeconds(50.0);
            TimeSpan cpu1 = TimeSpan.FromSeconds(5.0);

            double? cpu = CpuCalculator.CalculateNormalizedCpuPercent(cpu0, cpu1, t0, t1, 4);
            Assert(!cpu.HasValue, "TC-002: Drop in CPU time must return null/unavailable, not negative or 0");

            // Zero delta time
            double? cpuZeroTime = CpuCalculator.CalculateNormalizedCpuPercent(cpu0, cpu0, t0, t0, 4);
            Assert(!cpuZeroTime.HasValue, "TC-002: Zero wall-clock delta must return null");

            Console.WriteLine("  [PASS] TC-002: Process exit/restart or missing samples marked unavailable (not zero)");
        }

        private static void Test_CpuCalculation_Clamping()
        {
            DateTime t0 = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(1.0);

            // Jitter scenario: reported CPU time exceeds wall * cores
            TimeSpan cpu0 = TimeSpan.FromSeconds(0.0);
            TimeSpan cpu1 = TimeSpan.FromSeconds(10.0);
            int logicalCores = 2; // capacity is 2.0s, delta is 10.0s

            double? cpu = CpuCalculator.CalculateNormalizedCpuPercent(cpu0, cpu1, t0, t1, logicalCores);
            Assert(cpu.HasValue && Math.Abs(cpu.Value - 100.0) < 0.01, "CPU% should clamp to 100.0%");
            Console.WriteLine("  [PASS] TC-001b: Jitter anomaly correctly clamped to 100.0%");
        }

        private static void Test_ProcessIdentity_GuardsAgainstPidReuse()
        {
            int pid = 4820;
            DateTime start1 = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
            DateTime start2 = new DateTime(2026, 10, 26, 11, 30, 0, DateTimeKind.Utc);

            string id1 = CpuCalculator.BuildProcessIdentity(pid, start1);
            string id2 = CpuCalculator.BuildProcessIdentity(pid, start2);

            Assert(id1 != id2, "TC-003: Different start times for the same PID must produce distinct identities");
            Assert(id1.StartsWith("4820_"), "Identity must contain PID prefix");
            Console.WriteLine("  [PASS] TC-003: Process start identity protects against PID reuse");
        }

        private static void Test_MetricScope_Definitions()
        {
            Assert(CpuCalculator.NormalizedDefinition.Contains("logical cores"), "CPU metric definition must specify logical core normalization");
            Assert(CpuCalculator.WorkingSetDefinition.Contains("Working Set"), "Memory metric definition must state Working Set");
            Console.WriteLine("  [PASS] TC-025: Metric definitions and scope explicitly defined");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}

