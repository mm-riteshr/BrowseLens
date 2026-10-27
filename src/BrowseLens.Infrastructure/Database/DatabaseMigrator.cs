using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BrowseLens.Infrastructure.Database
{
    public class DatabaseMigrator
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public DatabaseMigrator(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<int> MigrateAsync()
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            using var tx = conn.BeginTransaction();

            // 1. Ensure migrations tracking table exists
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS SchemaMigrations (
                        Version INTEGER PRIMARY KEY,
                        AppliedAtUtc TEXT NOT NULL,
                        Description TEXT NOT NULL
                    );";
                await cmd.ExecuteNonQueryAsync();
            }

            int currentVersion = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    currentVersion = Convert.ToInt32(result);
                }
            }

            // 2. Apply Migration 1: Initial Schema
            if (currentVersion < 1)
            {
                await ApplyMigration1Async(conn, tx);
                currentVersion = 1;
            }

            tx.Commit();
            return currentVersion;
        }

        private async Task ApplyMigration1Async(SqliteConnection conn, SqliteTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                -- Browser Instances
                CREATE TABLE IF NOT EXISTS BrowserInstances (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BrowserType TEXT NOT NULL,
                    InstanceKey TEXT NULL,
                    FirstSeenUtc TEXT NOT NULL,
                    LastSeenUtc TEXT NOT NULL,
                    ConnectionStatus TEXT NOT NULL
                );

                -- Browsing Sessions
                CREATE TABLE IF NOT EXISTS BrowsingSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BrowserInstanceId INTEGER NULL REFERENCES BrowserInstances(Id) ON DELETE SET NULL,
                    WindowKey TEXT NOT NULL,
                    TabKey TEXT NOT NULL,
                    Url TEXT NOT NULL,
                    NormalizedDomain TEXT NOT NULL,
                    PageTitle TEXT NOT NULL,
                    StartedAtUtc TEXT NOT NULL,
                    EndedAtUtc TEXT NULL,
                    ForegroundDurationMs INTEGER NOT NULL DEFAULT 0,
                    EndReason TEXT NOT NULL DEFAULT '',
                    WasUrlRedacted INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL
                );

                -- Process Performance Samples
                CREATE TABLE IF NOT EXISTS ProcessSamples (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BrowserType TEXT NOT NULL,
                    ProcessId INTEGER NOT NULL,
                    ProcessStartIdentity TEXT NOT NULL,
                    SampledAtUtc TEXT NOT NULL,
                    CpuPercent REAL NULL,
                    MemoryBytes INTEGER NULL,
                    CpuMetricDefinition TEXT NOT NULL,
                    MemoryMetricDefinition TEXT NOT NULL
                );

                -- Application Settings
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );

                -- Excluded Patterns
                CREATE TABLE IF NOT EXISTS ExcludedPatterns (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Pattern TEXT NOT NULL UNIQUE,
                    CreatedAtUtc TEXT NOT NULL,
                    Enabled INTEGER NOT NULL DEFAULT 1
                );

                -- Indexes for fast queries and aggregations
                CREATE INDEX IF NOT EXISTS IX_BrowsingSessions_StartedAtUtc ON BrowsingSessions(StartedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_BrowsingSessions_Domain_Started ON BrowsingSessions(NormalizedDomain, StartedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_BrowsingSessions_EndedAtUtc ON BrowsingSessions(EndedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_ProcessSamples_SampledAtUtc ON ProcessSamples(SampledAtUtc);
                CREATE INDEX IF NOT EXISTS IX_ProcessSamples_Identity ON ProcessSamples(ProcessId, ProcessStartIdentity, SampledAtUtc);

                -- Seed default settings
                INSERT OR IGNORE INTO AppSettings (Key, Value, UpdatedAtUtc) VALUES
                    ('RetentionDays', '30', datetime('now')),
                    ('UrlRedactionEnabled', 'true', datetime('now')),
                    ('TrackingPaused', 'false', datetime('now'));

                -- Record Migration 1
                INSERT INTO SchemaMigrations (Version, AppliedAtUtc, Description)
                VALUES (1, datetime('now'), 'Initial schema baseline (sessions, processes, settings, exclusions)');
            ";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

