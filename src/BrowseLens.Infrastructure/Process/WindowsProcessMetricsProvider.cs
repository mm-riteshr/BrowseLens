using System;
using System.Collections.Generic;
using System.Diagnostics;
using BrowseLens.Core.Models;
using BrowseLens.Core.TimeAccounting;

namespace BrowseLens.Infrastructure.Process
{
    public class ProcessSampleCache
    {
        public TimeSpan TotalProcessorTime { get; set; }
        public DateTime SampledAtUtc { get; set; }
    }

    public class BrowserResourceSummary
    {
        public BrowserType Browser { get; set; }
        public int ProcessCount { get; set; }
        public double? TotalCpuPercent { get; set; }
        public long TotalMemoryWorkingSetBytes { get; set; }
        public double TotalMemoryWorkingSetMb
        {
            get { return TotalMemoryWorkingSetBytes / (1024.0 * 1024.0); }
        }
        public DateTime SampledAtUtc { get; set; }
        public List<ProcessMetricSample> IndividualSamples { get; set; }

        public BrowserResourceSummary()
        {
            IndividualSamples = new List<ProcessMetricSample>();
            SampledAtUtc = DateTime.UtcNow;
        }
    }

    public class WindowsProcessMetricsProvider
    {
        private readonly Dictionary<string, ProcessSampleCache> _sampleCache = new Dictionary<string, ProcessSampleCache>();
        private readonly int _logicalCoreCount;

        public WindowsProcessMetricsProvider()
        {
            _logicalCoreCount = Environment.ProcessorCount;
        }

        public WindowsProcessMetricsProvider(int logicalCoreCount)
        {
            _logicalCoreCount = logicalCoreCount > 0 ? logicalCoreCount : Environment.ProcessorCount;
        }

        public BrowserResourceSummary SampleBrowserProcesses(BrowserType browserType)
        {
            DateTime nowUtc = DateTime.UtcNow;
            string processName = browserType == BrowserType.Edge ? "msedge" : "chrome";

            var summary = new BrowserResourceSummary
            {
                Browser = browserType,
                SampledAtUtc = nowUtc
            };

            System.Diagnostics.Process[] processes;
            try
            {
                processes = System.Diagnostics.Process.GetProcessesByName(processName);
            }
            catch
            {
                processes = new System.Diagnostics.Process[0];
            }

            summary.ProcessCount = processes.Length;
            if (processes.Length == 0)
            {
                summary.TotalCpuPercent = null;
                return summary;
            }

            double totalCpu = 0.0;
            bool anyCpuMeasured = false;
            long totalWorkingSet = 0;
            var currentLiveIdentities = new HashSet<string>();

            foreach (var p in processes)
            {
                try
                {
                    int pid = p.Id;
                    DateTime startTimeUtc;
                    TimeSpan cpuTime;
                    long workingSet;

                    try
                    {
                        startTimeUtc = p.StartTime.ToUniversalTime();
                        cpuTime = p.TotalProcessorTime;
                        workingSet = p.WorkingSet64;
                    }
                    catch
                    {
                        // Process may have exited or access is restricted
                        continue;
                    }

                    string identity = CpuCalculator.BuildProcessIdentity(pid, startTimeUtc);
                    currentLiveIdentities.Add(identity);

                    double? cpuPercent = null;
                    if (_sampleCache.ContainsKey(identity))
                    {
                        var prev = _sampleCache[identity];
                        cpuPercent = CpuCalculator.CalculateNormalizedCpuPercent(
                            prev.TotalProcessorTime,
                            cpuTime,
                            prev.SampledAtUtc,
                            nowUtc,
                            _logicalCoreCount);

                        if (cpuPercent.HasValue)
                        {
                            totalCpu += cpuPercent.Value;
                            anyCpuMeasured = true;
                        }
                    }

                    _sampleCache[identity] = new ProcessSampleCache
                    {
                        TotalProcessorTime = cpuTime,
                        SampledAtUtc = nowUtc
                    };

                    totalWorkingSet += workingSet;

                    var sample = new ProcessMetricSample
                    {
                        Browser = browserType,
                        ProcessId = pid,
                        ProcessStartIdentity = identity,
                        SampledAtUtc = nowUtc,
                        CpuPercent = cpuPercent,
                        MemoryWorkingSetBytes = workingSet
                    };

                    summary.IndividualSamples.Add(sample);
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Clean up cache entries for processes that exited
            var deadIdentities = new List<string>();
            foreach (var key in _sampleCache.Keys)
            {
                if (!currentLiveIdentities.Contains(key))
                {
                    deadIdentities.Add(key);
                }
            }
            foreach (var dead in deadIdentities)
            {
                _sampleCache.Remove(dead);
            }

            summary.TotalMemoryWorkingSetBytes = totalWorkingSet;
            summary.TotalCpuPercent = anyCpuMeasured ? (double?)Math.Round(totalCpu, 2) : null;

            return summary;
        }
    }
}

