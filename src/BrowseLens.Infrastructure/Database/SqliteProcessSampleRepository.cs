using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace BrowseLens.Infrastructure.Database
{
    public class SqliteProcessSampleRepository : IProcessSampleRepository
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public SqliteProcessSampleRepository(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task InsertSampleAsync(ProcessSampleEntity sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));

            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ProcessSamples (
                    BrowserType, ProcessId, ProcessStartIdentity, SampledAtUtc,
                    CpuPercent, MemoryBytes, CpuMetricDefinition, MemoryMetricDefinition
                ) VALUES (
                    @browser, @pid, @identity, @sampledAt,
                    @cpu, @mem, @cpuDef, @memDef
                );
                SELECT last_insert_rowid();";

            AddSampleParameters(cmd, sample);
            var result = await cmd.ExecuteScalarAsync();
            sample.Id = Convert.ToInt64(result);
        }

        public async Task InsertSamplesBatchAsync(IEnumerable<ProcessSampleEntity> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));

            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var tx = conn.BeginTransaction();

            foreach (var sample in samples)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ProcessSamples (
                        BrowserType, ProcessId, ProcessStartIdentity, SampledAtUtc,
                        CpuPercent, MemoryBytes, CpuMetricDefinition, MemoryMetricDefinition
                    ) VALUES (
                        @browser, @pid, @identity, @sampledAt,
                        @cpu, @mem, @cpuDef, @memDef
                    );";

                AddSampleParameters(cmd, sample);
                await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        public async Task<IReadOnlyList<ProcessSampleEntity>> GetRecentSamplesAsync(string browserType, int count = 50)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM ProcessSamples
                WHERE BrowserType = @browser
                ORDER BY SampledAtUtc DESC
                LIMIT @count;";

            cmd.Parameters.AddWithValue("@browser", browserType);
            cmd.Parameters.AddWithValue("@count", count);

            var list = new List<ProcessSampleEntity>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int cpuIdx = reader.GetOrdinal("CpuPercent");
                int memIdx = reader.GetOrdinal("MemoryBytes");

                list.Add(new ProcessSampleEntity
                {
                    Id = reader.GetInt64(reader.GetOrdinal("Id")),
                    BrowserType = reader.GetString(reader.GetOrdinal("BrowserType")),
                    ProcessId = reader.GetInt32(reader.GetOrdinal("ProcessId")),
                    ProcessStartIdentity = reader.GetString(reader.GetOrdinal("ProcessStartIdentity")),
                    SampledAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("SampledAtUtc"))).ToUniversalTime(),
                    CpuPercent = reader.IsDBNull(cpuIdx) ? null : reader.GetDouble(cpuIdx),
                    MemoryBytes = reader.IsDBNull(memIdx) ? null : reader.GetInt64(memIdx),
                    CpuMetricDefinition = reader.GetString(reader.GetOrdinal("CpuMetricDefinition")),
                    MemoryMetricDefinition = reader.GetString(reader.GetOrdinal("MemoryMetricDefinition"))
                });
            }
            return list;
        }

        public async Task<int> PurgeExpiredSamplesAsync(DateTime cutoffUtc)
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ProcessSamples WHERE SampledAtUtc < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoffUtc.ToString("o"));

            return await cmd.ExecuteNonQueryAsync();
        }

        private static void AddSampleParameters(SqliteCommand cmd, ProcessSampleEntity sample)
        {
            cmd.Parameters.AddWithValue("@browser", sample.BrowserType);
            cmd.Parameters.AddWithValue("@pid", sample.ProcessId);
            cmd.Parameters.AddWithValue("@identity", sample.ProcessStartIdentity);
            cmd.Parameters.AddWithValue("@sampledAt", sample.SampledAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@cpu", sample.CpuPercent.HasValue ? (object)sample.CpuPercent.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@mem", sample.MemoryBytes.HasValue ? (object)sample.MemoryBytes.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@cpuDef", sample.CpuMetricDefinition);
            cmd.Parameters.AddWithValue("@memDef", sample.MemoryMetricDefinition);
        }
    }
}

