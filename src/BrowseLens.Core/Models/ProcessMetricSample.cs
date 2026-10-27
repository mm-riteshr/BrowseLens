using System;

namespace BrowseLens.Core.Models
{
    public class ProcessMetricSample
    {
        public BrowserType Browser { get; set; }
        public int ProcessId { get; set; }
        public string ProcessStartIdentity { get; set; }
        public DateTime SampledAtUtc { get; set; }
        public double? CpuPercent { get; set; }
        public long? MemoryWorkingSetBytes { get; set; }
        public string CpuMetricDefinition { get; set; }
        public string MemoryMetricDefinition { get; set; }

        public bool IsAvailable
        {
            get { return CpuPercent.HasValue && MemoryWorkingSetBytes.HasValue; }
        }

        public double? MemoryWorkingSetMb
        {
            get
            {
                if (!MemoryWorkingSetBytes.HasValue) return null;
                return MemoryWorkingSetBytes.Value / (1024.0 * 1024.0);
            }
        }

        public ProcessMetricSample()
        {
            Browser = BrowserType.Unknown;
            ProcessStartIdentity = string.Empty;
            SampledAtUtc = DateTime.UtcNow;
            CpuMetricDefinition = "Normalized % of total machine capacity across all logical cores";
            MemoryMetricDefinition = "Working Set (Physical Memory in bytes)";
        }
    }
}

