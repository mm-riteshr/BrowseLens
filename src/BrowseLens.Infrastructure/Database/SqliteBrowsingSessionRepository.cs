using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace BrowseLens.Infrastructure.Database
{
    public class SqliteBrowsingSessionRepository : IBrowsingSessionRepository
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public SqliteBrowsingSessionRepository(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<long> InsertSessionAsync(BrowsingSessionEntity session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BrowsingSessions (
                    BrowserInstanceId, WindowKey, TabKey, Url, NormalizedDomain, PageTitle,
                    StartedAtUtc, EndedAtUtc, ForegroundDurationMs, EndReason, WasUrlRedacted, CreatedAtUtc
                ) VALUES (
                    @instanceId, @windowKey, @tabKey, @url, @domain, @title,
                    @startedAt, @endedAt, @durationMs, @endReason, @redacted, @createdAt
                );
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@instanceId", (object?)session.BrowserInstanceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@windowKey", session.WindowKey);
            cmd.Parameters.AddWithValue("@tabKey", session.TabKey);
            cmd.Parameters.AddWithValue("@url", session.Url);
            cmd.Parameters.AddWithValue("@domain", session.NormalizedDomain);
            cmd.Parameters.AddWithValue("@title", session.PageTitle);
            cmd.Parameters.AddWithValue("@startedAt", session.StartedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@endedAt", session.EndedAtUtc.HasValue ? (object)session.EndedAtUtc.Value.ToString("o") : DBNull.Value);
            cmd.Parameters.AddWithValue("@durationMs", session.ForegroundDurationMs);
            cmd.Parameters.AddWithValue("@endReason", session.EndReason ?? "");
            cmd.Parameters.AddWithValue("@redacted", session.WasUrlRedacted ? 1 : 0);
            cmd.Parameters.AddWithValue("@createdAt", session.CreatedAtUtc.ToString("o"));

            var result = await cmd.ExecuteScalarAsync();
            session.Id = Convert.ToInt64(result);
            return session.Id;
        }

        public async Task UpdateSessionAsync(BrowsingSessionEntity session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BrowsingSessions
                SET EndedAtUtc = @endedAt,
                    ForegroundDurationMs = @durationMs,
                    EndReason = @endReason,
                    PageTitle = @title
                WHERE Id = @id;";

            cmd.Parameters.AddWithValue("@id", session.Id);
            cmd.Parameters.AddWithValue("@endedAt", session.EndedAtUtc.HasValue ? (object)session.EndedAtUtc.Value.ToString("o") : DBNull.Value);
            cmd.Parameters.AddWithValue("@durationMs", session.ForegroundDurationMs);
            cmd.Parameters.AddWithValue("@endReason", session.EndReason ?? "");
            cmd.Parameters.AddWithValue("@title", session.PageTitle);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<BrowsingSessionEntity?> GetSessionByIdAsync(long id)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM BrowsingSessions WHERE Id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapSession(reader);
            }
            return null;
        }

        public async Task<BrowsingSessionEntity?> GetActiveSessionAsync(string windowKey)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM BrowsingSessions
                WHERE WindowKey = @windowKey AND EndedAtUtc IS NULL
                ORDER BY StartedAtUtc DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@windowKey", windowKey);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapSession(reader);
            }
            return null;
        }

        public async Task<IReadOnlyList<BrowsingSessionEntity>> GetSessionsAsync(DateTime fromUtc, DateTime toUtc, string? domain = null)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();

            string sql = "SELECT * FROM BrowsingSessions WHERE StartedAtUtc >= @from AND StartedAtUtc <= @to";
            if (!string.IsNullOrEmpty(domain))
            {
                sql += " AND NormalizedDomain = @domain";
                cmd.Parameters.AddWithValue("@domain", domain.ToLowerInvariant());
            }
            sql += " ORDER BY StartedAtUtc DESC;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@from", fromUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@to", toUtc.ToString("o"));

            var list = new List<BrowsingSessionEntity>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(MapSession(reader));
            }
            return list;
        }

        public async Task<long> CloseOrphanedSessionsAsync(DateTime recoveryCutoffUtc, string reason)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BrowsingSessions
                SET EndedAtUtc = StartedAtUtc,
                    ForegroundDurationMs = 0,
                    EndReason = @reason
                WHERE EndedAtUtc IS NULL;";
            cmd.Parameters.AddWithValue("@reason", reason);

            int rows = await cmd.ExecuteNonQueryAsync();
            return rows;
        }

        public async Task<int> DeleteSessionsAsync(DateTime? beforeUtc = null, long? id = null)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();

            if (id.HasValue)
            {
                cmd.CommandText = "DELETE FROM BrowsingSessions WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id.Value);
            }
            else if (beforeUtc.HasValue)
            {
                cmd.CommandText = "DELETE FROM BrowsingSessions WHERE StartedAtUtc < @before;";
                cmd.Parameters.AddWithValue("@before", beforeUtc.Value.ToString("o"));
            }
            else
            {
                cmd.CommandText = "DELETE FROM BrowsingSessions;";
            }

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<long> GetTotalForegroundDurationMsAsync(DateTime fromUtc, DateTime toUtc)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(ForegroundDurationMs), 0)
                FROM BrowsingSessions
                WHERE StartedAtUtc >= @from AND StartedAtUtc <= @to;";
            cmd.Parameters.AddWithValue("@from", fromUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@to", toUtc.ToString("o"));

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public async Task<IReadOnlyList<DomainUsageSummary>> GetTopDomainsAsync(DateTime fromUtc, DateTime toUtc, int limit = 10)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT NormalizedDomain,
                       COALESCE(SUM(ForegroundDurationMs), 0) AS TotalDurationMs,
                       COUNT(*) AS VisitCount
                FROM BrowsingSessions
                WHERE StartedAtUtc >= @from AND StartedAtUtc <= @to AND NormalizedDomain != ''
                GROUP BY NormalizedDomain
                ORDER BY TotalDurationMs DESC
                LIMIT @limit;";

            cmd.Parameters.AddWithValue("@from", fromUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@to", toUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@limit", limit);

            var list = new List<DomainUsageSummary>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new DomainUsageSummary
                {
                    Domain = reader.GetString(reader.GetOrdinal("NormalizedDomain")),
                    TotalForegroundDurationMs = reader.GetInt64(reader.GetOrdinal("TotalDurationMs")),
                    VisitCount = reader.GetInt32(reader.GetOrdinal("VisitCount"))
                });
            }
            return list;
        }

        private static BrowsingSessionEntity MapSession(SqliteDataReader reader)
        {
            int instIdx = reader.GetOrdinal("BrowserInstanceId");
            int endedIdx = reader.GetOrdinal("EndedAtUtc");

            return new BrowsingSessionEntity
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                BrowserInstanceId = reader.IsDBNull(instIdx) ? null : reader.GetInt64(instIdx),
                WindowKey = reader.GetString(reader.GetOrdinal("WindowKey")),
                TabKey = reader.GetString(reader.GetOrdinal("TabKey")),
                Url = reader.GetString(reader.GetOrdinal("Url")),
                NormalizedDomain = reader.GetString(reader.GetOrdinal("NormalizedDomain")),
                PageTitle = reader.GetString(reader.GetOrdinal("PageTitle")),
                StartedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("StartedAtUtc"))).ToUniversalTime(),
                EndedAtUtc = reader.IsDBNull(endedIdx) ? null : DateTime.Parse(reader.GetString(endedIdx)).ToUniversalTime(),
                ForegroundDurationMs = reader.GetInt64(reader.GetOrdinal("ForegroundDurationMs")),
                EndReason = reader.GetString(reader.GetOrdinal("EndReason")),
                WasUrlRedacted = reader.GetInt32(reader.GetOrdinal("WasUrlRedacted")) == 1,
                CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))).ToUniversalTime()
            };
        }
    }
}

