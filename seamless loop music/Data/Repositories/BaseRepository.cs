using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace seamless_loop_music.Data.Repositories
{
    public abstract class BaseRepository
    {
        protected readonly string _connectionString;
        protected readonly string _dbPath;

        protected BaseRepository()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(baseDir, "Data");
            
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            _dbPath = Path.Combine(dataDir, "LoopData.db");
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;";
        }

        protected IDbConnection GetConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}

