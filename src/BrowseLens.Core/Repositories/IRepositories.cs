using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;

namespace BrowseLens.Core.Repositories
{
    public interface IBrowsingSessionRepository
    {
        Task<long> InsertSessionAsync(BrowsingSessionEntity session);
        Task UpdateSessionAsync(BrowsingSessionEntity session);
        Task<BrowsingSessionEntity?> GetSessionByIdAsync(long id);
        Task<BrowsingSessionEntity?> GetActiveSessionAsync(string windowKey);
        Task<IReadOnlyList<BrowsingSessionEntity>> GetSessionsAsync(DateTime fromUtc, DateTime toUtc, string? domain = null);
        Task<long> CloseOrphanedSessionsAsync(DateTime recoveryCutoffUtc, string reason);
        Task<int> DeleteSessionsAsync(DateTime? beforeUtc = null, long? id = null);
        Task<long> GetTotalForegroundDurationMsAsync(DateTime fromUtc, DateTime toUtc);
        Task<IReadOnlyList<DomainUsageSummary>> GetTopDomainsAsync(DateTime fromUtc, DateTime toUtc, int limit = 10);
    }

    public class DomainUsageSummary
    {
        public string Domain { get; set; } = string.Empty;
        public long TotalForegroundDurationMs { get; set; }
        public int VisitCount { get; set; }
    }

    public interface IProcessSampleRepository
    {
        Task InsertSampleAsync(ProcessSampleEntity sample);
        Task InsertSamplesBatchAsync(IEnumerable<ProcessSampleEntity> samples);
        Task<IReadOnlyList<ProcessSampleEntity>> GetRecentSamplesAsync(string browserType, int count = 50);
        Task<int> PurgeExpiredSamplesAsync(DateTime cutoffUtc);
    }

    public interface ISettingsRepository
    {
        Task<string?> GetSettingAsync(string key);
        Task SetSettingAsync(string key, string value);
        Task<IReadOnlyList<ExcludedPatternEntity>> GetExcludedPatternsAsync();
        Task AddExcludedPatternAsync(string pattern);
        Task RemoveExcludedPatternAsync(string pattern);
    }

    public interface IBrowserInstanceRepository
    {
        Task<long> RegisterOrUpdateInstanceAsync(BrowserInstanceEntity instance);
        Task<BrowserInstanceEntity?> GetInstanceAsync(string browserType, string? instanceKey);
        Task UpdateStatusAsync(long id, string status, DateTime lastSeenUtc);
    }
}

