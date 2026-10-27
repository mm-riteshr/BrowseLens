using System;

namespace BrowseLens.Core.TimeAccounting
{
    public static class CpuCalculator
    {
        public const string NormalizedDefinition = "Normalized % of total machine capacity across all logical cores";
        public const string WorkingSetDefinition = "Working Set (Physical RAM allocated including shared pages)";

        /// <summary>
        /// Calculates CPU utilization normalized against all logical cores on the machine.
        /// Formula: ((delta CPU time) / (delta wall-clock time * logical cores)) * 100
        /// </summary>
        public static double? CalculateNormalizedCpuPercent(
            TimeSpan previousTotalProcessorTime,
            TimeSpan currentTotalProcessorTime,
            DateTime previousSampleTimeUtc,
            DateTime currentSampleTimeUtc,
            int logicalCoreCount)
        {
            if (logicalCoreCount <= 0)
            {
                return null;
            }

            double deltaWallSeconds = (currentSampleTimeUtc - previousSampleTimeUtc).TotalSeconds;
            if (deltaWallSeconds <= 0.0001)
            {
                return null;
            }

            double deltaCpuSeconds = (currentTotalProcessorTime - previousTotalProcessorTime).TotalSeconds;
            if (deltaCpuSeconds < 0.0)
            {
                // Indicates process exit/restart or PID reuse with lower total CPU time
                return null;
            }

            double totalCapacitySeconds = deltaWallSeconds * logicalCoreCount;
            double rawPercent = (deltaCpuSeconds / totalCapacitySeconds) * 100.0;

            // Clamp transient anomalies caused by OS timer jitter
            if (rawPercent < 0.0) rawPercent = 0.0;
            if (rawPercent > 100.0) rawPercent = 100.0;

            return Math.Round(rawPercent, 2);
        }

        /// <summary>
        /// Generates a unique identity string combining PID and start timestamp to guard against PID reuse.
        /// </summary>
        public static string BuildProcessIdentity(int pid, DateTime startTimeUtc)
        {
            return string.Format("{0}_{1}", pid, startTimeUtc.Ticks);
        }
    }
}

