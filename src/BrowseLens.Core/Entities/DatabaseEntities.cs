using System;

namespace BrowseLens.Core.Entities
{
    public class BrowsingSessionEntity
    {
        public long Id { get; set; }
        public long? BrowserInstanceId { get; set; }
        public string WindowKey { get; set; } = string.Empty;
        public string TabKey { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string NormalizedDomain { get; set; } = string.Empty;
        public string PageTitle { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
        public long ForegroundDurationMs { get; set; }
        public string EndReason { get; set; } = string.Empty;
        public bool WasUrlRedacted { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsOpen => !EndedAtUtc.HasValue;
    }

    public class BrowserInstanceEntity
    {
        public long Id { get; set; }
        public string BrowserType { get; set; } = string.Empty;
        public string? InstanceKey { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public string ConnectionStatus { get; set; } = string.Empty;
    }

    public class ProcessSampleEntity
    {
        public long Id { get; set; }
        public string BrowserType { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ProcessStartIdentity { get; set; } = string.Empty;
        public DateTime SampledAtUtc { get; set; }
        public double? CpuPercent { get; set; }
        public long? MemoryBytes { get; set; }
        public string CpuMetricDefinition { get; set; } = string.Empty;
        public string MemoryMetricDefinition { get; set; } = string.Empty;
    }

    public class AppSettingEntity
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class ExcludedPatternEntity
    {
        public long Id { get; set; }
        public string Pattern { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public bool Enabled { get; set; } = true;
    }
}

