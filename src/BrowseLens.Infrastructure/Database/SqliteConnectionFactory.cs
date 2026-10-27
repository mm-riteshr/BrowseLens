using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BrowseLens.Infrastructure.Database
{
    public interface ISqliteConnectionFactory
    {
        Task<SqliteConnection> CreateConnectionAsync();
        string DatabasePath { get; }
    }

    public class SqliteConnectionFactory : ISqliteConnectionFactory
    {
        private readonly string _connectionString;
        private readonly string _databasePath;
        private readonly bool _isMemory;

        public string DatabasePath => _databasePath;

        public SqliteConnectionFactory(string? customDbPath = null)
        {
            if (string.IsNullOrEmpty(customDbPath))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appDir = Path.Combine(localAppData, "BrowseLens");
                if (!Directory.Exists(appDir))
                {
                    Directory.CreateDirectory(appDir);
                }
                _databasePath = Path.Combine(appDir, "browselens.db");
                _connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Default
                }.ToString();
                _isMemory = false;
            }
            else if (customDbPath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                _databasePath = ":memory:";
                _connectionString = "Data Source=:memory:;Mode=Memory;Cache=Shared";
                _isMemory = true;
            }
            else
            {
                string? dir = Path.GetDirectoryName(customDbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                _databasePath = customDbPath;
                _connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString();
                _isMemory = false;
            }
        }

        public async Task<SqliteConnection> CreateConnectionAsync()
        {
            var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            if (!_isMemory)
            {
                cmd.CommandText += " PRAGMA journal_mode = WAL;";
            }
            await cmd.ExecuteNonQueryAsync();

            return conn;
        }
    }
}

