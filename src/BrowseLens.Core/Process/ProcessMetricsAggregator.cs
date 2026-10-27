using System;
using System.Collections.Generic;
using System.Linq;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;

namespace BrowseLens.Core.Process
{
    public class AggregatedMetricSummary
    {
        public string BrowserType { get; set; } = string.Empty;
        public int ProcessCount { get; set; }
        public double? CurrentCpuPercent { get; set; }
        public double? AverageCpuPercent { get; set; }
        public double? PeakCpuPercent { get; set; }
        public long CurrentWorkingSetBytes { get; set; }
        public long PeakWorkingSetBytes { get; set; }
        public double CurrentWorkingSetMb => CurrentWorkingSetBytes / (1024.0 * 1024.0);
        public double PeakWorkingSetMb => PeakWorkingSetBytes / (1024.0 * 1024.0);
        public DateTime SampledAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class ProcessMetricsAggregator
    {
        private readonly object _lock = new object();
        private readonly int _maxWindowMinutes;
        private readonly List<ProcessSampleEntity> _sampleWindow = new List<ProcessSampleEntity>();

        public ProcessMetricsAggregator(int maxWindowMinutes = 15)
        {
            _maxWindowMinutes = maxWindowMinutes > 0 ? maxWindowMinutes : 15;
        }

        public void AddSamples(IEnumerable<ProcessSampleEntity> samples)
        {
            if (samples == null) return;

            DateTime cutoff = DateTime.UtcNow.AddMinutes(-_maxWindowMinutes);
            lock (_lock)
            {
                foreach (var sample in samples)
                {
                    _sampleWindow.Add(sample);
                }

                // Evict samples older than the sliding window
                _sampleWindow.RemoveAll(s => s.SampledAtUtc < cutoff);
            }
        }

        public AggregatedMetricSummary GetAggregatedSummary(string browserType, int recentMinutes = 5)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-recentMinutes);
            lock (_lock)
            {
                var matching = _sampleWindow
                    .Where(s => s.BrowserType.Equals(browserType, StringComparison.OrdinalIgnoreCase) && s.SampledAtUtc >= cutoff)
                    .ToList();

                var summary = new AggregatedMetricSummary
                {
                    BrowserType = browserType,
                    SampledAtUtc = DateTime.UtcNow
                };

                if (matching.Count == 0)
                {
                    return summary;
                }

                // Group by sample timestamp to aggregate per-cycle browser metrics
                var timestampGroups = matching
                    .GroupBy(s => s.SampledAtUtc)
                    .OrderBy(g => g.Key)
                    .ToList();

                var latestGroup = timestampGroups.LastOrDefault();
                if (latestGroup != null)
                {
                    summary.ProcessCount = latestGroup.Count();
                    summary.CurrentWorkingSetBytes = latestGroup.Sum(s => s.MemoryBytes ?? 0);

                    var validCpus = latestGroup.Where(s => s.CpuPercent.HasValue).Select(s => s.CpuPercent!.Value).ToList();
                    summary.CurrentCpuPercent = validCpus.Count > 0 ? Math.Round(validCpus.Sum(), 2) : null;
                }

                // Calculate rolling metrics across all sample cycles in the window
                var cycleCpuSums = new List<double>();
                long peakWorkingSet = 0;

                foreach (var group in timestampGroups)
                {
                    long cycleWorkingSet = group.Sum(s => s.MemoryBytes ?? 0);
                    if (cycleWorkingSet > peakWorkingSet)
                    {
                        peakWorkingSet = cycleWorkingSet;
                    }

                    var cpus = group.Where(s => s.CpuPercent.HasValue).Select(s => s.CpuPercent!.Value).ToList();
                    if (cpus.Count > 0)
                    {
                        cycleCpuSums.Add(cpus.Sum());
                    }
                }

                summary.PeakWorkingSetBytes = peakWorkingSet;
                if (cycleCpuSums.Count > 0)
                {
                    summary.AverageCpuPercent = Math.Round(cycleCpuSums.Average(), 2);
                    summary.PeakCpuPercent = Math.Round(cycleCpuSums.Max(), 2);
                }

                return summary;
            }
        }

        public IReadOnlyList<ProcessSampleEntity> GetCurrentWindowSamples()
        {
            lock (_lock)
            {
                return _sampleWindow.ToList();
            }
        }
    }
}

