using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace BrowseLens.Infrastructure.Database
{
    public class SqliteSettingsRepository : ISettingsRepository
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public SqliteSettingsRepository(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key LIMIT 1;";
            cmd.Parameters.AddWithValue("@key", key);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value ? result.ToString() : null;
        }

        public async Task SetSettingAsync(string key, string value)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AppSettings (Key, Value, UpdatedAtUtc)
                VALUES (@key, @value, @updatedAt)
                ON CONFLICT(Key) DO UPDATE SET
                    Value = excluded.Value,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;";

            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<ExcludedPatternEntity>> GetExcludedPatternsAsync()
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM ExcludedPatterns WHERE Enabled = 1 ORDER BY Pattern ASC;";

            var list = new List<ExcludedPatternEntity>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ExcludedPatternEntity
                {
                    Id = reader.GetInt64(reader.GetOrdinal("Id")),
                    Pattern = reader.GetString(reader.GetOrdinal("Pattern")),
                    CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))).ToUniversalTime(),
                    Enabled = reader.GetInt32(reader.GetOrdinal("Enabled")) == 1
                });
            }
            return list;
        }

        public async Task AddExcludedPatternAsync(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return;

            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO ExcludedPatterns (Pattern, CreatedAtUtc, Enabled)
                VALUES (@pattern, @createdAt, 1);";

            cmd.Parameters.AddWithValue("@pattern", pattern.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveExcludedPatternAsync(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return;

            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ExcludedPatterns WHERE Pattern = @pattern;";
            cmd.Parameters.AddWithValue("@pattern", pattern.Trim().ToLowerInvariant());

            await cmd.ExecuteNonQueryAsync();
        }
    }

    public class SqliteBrowserInstanceRepository : IBrowserInstanceRepository
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public SqliteBrowserInstanceRepository(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<long> RegisterOrUpdateInstanceAsync(BrowserInstanceEntity instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            using var conn = await _connectionFactory.CreateConnectionAsync();

            if (instance.Id > 0)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE BrowserInstances
                    SET LastSeenUtc = @lastSeen,
                        ConnectionStatus = @status
                    WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", instance.Id);
                cmd.Parameters.AddWithValue("@lastSeen", instance.LastSeenUtc.ToString("o"));
                cmd.Parameters.AddWithValue("@status", instance.ConnectionStatus);
                await cmd.ExecuteNonQueryAsync();
                return instance.Id;
            }
            else
            {
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = @"
                    SELECT Id FROM BrowserInstances
                    WHERE BrowserType = @browser AND (InstanceKey = @key OR (@key IS NULL AND InstanceKey IS NULL))
                    LIMIT 1;";
                checkCmd.Parameters.AddWithValue("@browser", instance.BrowserType);
                checkCmd.Parameters.AddWithValue("@key", (object?)instance.InstanceKey ?? DBNull.Value);
                var existingId = await checkCmd.ExecuteScalarAsync();

                if (existingId != null && existingId != DBNull.Value)
                {
                    instance.Id = Convert.ToInt64(existingId);
                    using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = @"
                        UPDATE BrowserInstances
                        SET LastSeenUtc = @lastSeen,
                            ConnectionStatus = @status
                        WHERE Id = @id;";
                    updateCmd.Parameters.AddWithValue("@id", instance.Id);
                    updateCmd.Parameters.AddWithValue("@lastSeen", instance.LastSeenUtc.ToString("o"));
                    updateCmd.Parameters.AddWithValue("@status", instance.ConnectionStatus);
                    await updateCmd.ExecuteNonQueryAsync();
                    return instance.Id;
                }
                else
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO BrowserInstances (BrowserType, InstanceKey, FirstSeenUtc, LastSeenUtc, ConnectionStatus)
                        VALUES (@browser, @key, @firstSeen, @lastSeen, @status);
                        SELECT last_insert_rowid();";
                    insertCmd.Parameters.AddWithValue("@browser", instance.BrowserType);
                    insertCmd.Parameters.AddWithValue("@key", (object?)instance.InstanceKey ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@firstSeen", instance.FirstSeenUtc.ToString("o"));
                    insertCmd.Parameters.AddWithValue("@lastSeen", instance.LastSeenUtc.ToString("o"));
                    insertCmd.Parameters.AddWithValue("@status", instance.ConnectionStatus);

                    var result = await insertCmd.ExecuteScalarAsync();
                    instance.Id = Convert.ToInt64(result);
                    return instance.Id;
                }
            }
        }

        public async Task<BrowserInstanceEntity?> GetInstanceAsync(string browserType, string? instanceKey)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM BrowserInstances
                WHERE BrowserType = @browser AND (@key IS NULL OR InstanceKey = @key)
                ORDER BY LastSeenUtc DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@browser", browserType);
            cmd.Parameters.AddWithValue("@key", (object?)instanceKey ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int keyIdx = reader.GetOrdinal("InstanceKey");
                return new BrowserInstanceEntity
                {
                    Id = reader.GetInt64(reader.GetOrdinal("Id")),
                    BrowserType = reader.GetString(reader.GetOrdinal("BrowserType")),
                    InstanceKey = reader.IsDBNull(keyIdx) ? null : reader.GetString(keyIdx),
                    FirstSeenUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("FirstSeenUtc"))).ToUniversalTime(),
                    LastSeenUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastSeenUtc"))).ToUniversalTime(),
                    ConnectionStatus = reader.GetString(reader.GetOrdinal("ConnectionStatus"))
                };
            }
            return null;
        }

        public async Task UpdateStatusAsync(long id, string status, DateTime lastSeenUtc)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE BrowserInstances SET ConnectionStatus = @status, LastSeenUtc = @lastSeen WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@lastSeen", lastSeenUtc.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
        }
    }
}

