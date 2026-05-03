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

        protected BaseRepository() : this(null) { }

        protected BaseRepository(string customDbPath = null)
        {
            if (string.IsNullOrEmpty(customDbPath))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dataDir = Path.Combine(baseDir, "Data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                _dbPath = Path.Combine(dataDir, "LoopData.db");
            }
            else
            {
                if (customDbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                {
                    _connectionString = customDbPath;
                    _dbPath = customDbPath;
                    try { InitializeDatabase(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BaseRepository] Init Error: {ex.Message}"); }
                    return;
                }
                _dbPath = customDbPath;
            }
            _connectionString = $"Data Source={_dbPath};Version=3;Foreign Keys=True;Default Timeout=5;";

            try { InitializeDatabase(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BaseRepository] Init Error: {ex.Message}"); }
        }

        private void InitializeDatabase()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Execute("PRAGMA journal_mode=WAL;");
                    conn.Execute("PRAGMA foreign_keys=ON;");

                    // ── Artists 表 ──────────────────────────────────────────
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS Artists (
                            Id      INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name    TEXT NOT NULL UNIQUE,
                            CoverPath TEXT
                        );");

                    // ── Albums 表 ───────────────────────────────────────────
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS Albums (
                            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name      TEXT NOT NULL,
                            CoverPath TEXT,
                            UNIQUE(Name)
                        );");

                    // ── Tracks 表 (直接挂载 ArtistId) ──────────────────────
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS Tracks (
                            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                            FileName      TEXT NOT NULL,
                            FilePath      TEXT,
                            DisplayName   TEXT,
                            TotalSamples  INTEGER DEFAULT 0,
                            LastModified  DATETIME,
                            CoverPath     TEXT,
                            AlbumId       INTEGER,
                            ArtistId      INTEGER,
                            FOREIGN KEY(AlbumId) REFERENCES Albums(Id) ON DELETE SET NULL,
                            FOREIGN KEY(ArtistId) REFERENCES Artists(Id) ON DELETE SET NULL,
                            UNIQUE(FileName, TotalSamples)
                        );");

                    // ── LoopPoints 表（循环点，与 Tracks 1:1）────────────────
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS LoopPoints (
                            TrackId               INTEGER PRIMARY KEY,
                            LoopStart             INTEGER DEFAULT 0,
                            LoopEnd               INTEGER DEFAULT 0,
                            LoopCandidatesJson    TEXT,
                            AnalysisLastModified  DATETIME,
                            FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
                        );");

                    // ── UserRatings 表（用户评分，与 Tracks 1:1）─────────────
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS UserRatings (
                            TrackId      INTEGER PRIMARY KEY,
                            Rating       INTEGER DEFAULT 0,
                            LastModified DATETIME,
                            FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
                        );");

                    // ── Playlists & PlaylistItems（播放列表）────────────────
                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS Playlists (
                            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name           TEXT NOT NULL,
                            SortOrder      INTEGER DEFAULT 0,
                            CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP
                        );");

                    conn.Execute(@"
                        CREATE TABLE IF NOT EXISTS PlaylistItems (
                            PlaylistId INTEGER,
                            SongId     INTEGER,
                            SortOrder  INTEGER DEFAULT 0,
                            PRIMARY KEY(PlaylistId, SongId),
                            FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                            FOREIGN KEY(SongId)     REFERENCES Tracks(Id)    ON DELETE CASCADE
                        );");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[数据库初始化失败] {ex.Message}");
                throw; // 抛出异常以便上层知道初始化失败
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
