using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;

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
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;Default Timeout=5;";

            try { InitializeDatabase(); } catch { }
        }

        private void InitializeDatabase()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Execute("PRAGMA journal_mode=WAL;");
                    
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS LoopPoints (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            FileName TEXT NOT NULL,
                            FilePath TEXT,
                            DisplayName TEXT,
                            LoopStart INTEGER DEFAULT 0,
                            LoopEnd INTEGER DEFAULT 0,
                            TotalSamples INTEGER DEFAULT 0,
                            Artist TEXT,
                            Album TEXT,
                            AlbumArtist TEXT,
                            LastModified DATETIME,
                            LoopCandidatesJson TEXT,
                            IsLoved INTEGER DEFAULT 0,
                            Rating INTEGER DEFAULT 0,
                            UNIQUE(FileName, TotalSamples)
                        );");

                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS Playlists (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT NOT NULL,
                            FolderPath TEXT,
                            IsFolderLinked INTEGER DEFAULT 0,
                            SortOrder INTEGER DEFAULT 0,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        );");

                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS PlaylistItems (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            PlaylistId INTEGER NOT NULL,
                            SongId INTEGER NOT NULL,
                            SortOrder INTEGER DEFAULT 0,
                            FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                            FOREIGN KEY(SongId) REFERENCES LoopPoints(Id) ON DELETE CASCADE
                        );");

                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS PlaylistFolders (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            PlaylistId INTEGER NOT NULL,
                            FolderPath TEXT NOT NULL,
                            AddedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                            FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                            UNIQUE(PlaylistId, FolderPath)
                        );");

                    try
                    {
                        var columns = conn.Query<string>("PRAGMA table_info(LoopPoints)").ToList();
                        if (!columns.Contains("IsLoved"))
                            conn.Execute("ALTER TABLE LoopPoints ADD COLUMN IsLoved INTEGER DEFAULT 0;");
                        if (!columns.Contains("Rating"))
                            conn.Execute("ALTER TABLE LoopPoints ADD COLUMN Rating INTEGER DEFAULT 0;");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[数据库初始化失败] {ex.Message}");
            }
        }

        protected IDbConnection GetConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}

