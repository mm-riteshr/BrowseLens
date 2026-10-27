using System;

namespace BrowseLens.Core.Models
{
    public class BrowserEventEnvelope
    {
        public int SchemaVersion { get; set; }
        public string EventId { get; set; }
        public long Sequence { get; set; }
        public string Browser { get; set; }
        public string EventType { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string WindowKey { get; set; }
        public string TabKey { get; set; }
        public TabEventPayload Payload { get; set; }

        public BrowserEventEnvelope()
        {
            SchemaVersion = 1;
            EventId = Guid.NewGuid().ToString("N");
            Sequence = 0;
            Browser = "Chrome";
            EventType = "TabActivated";
            OccurredAtUtc = DateTime.UtcNow;
            WindowKey = "w-0";
            TabKey = "t-0";
            Payload = new TabEventPayload();
        }
    }
}

