using System;
using System.IO;
using System.Data;
using System.Data.SQLite;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data
{
    public interface IDatabaseHelper
    {
        IDbConnection GetConnection();
        void InitializeDatabase();

        // 兼容性接口 (Legacy Support)
        IEnumerable<MusicTrack> GetAllTracks();
        MusicTrack GetTrack(string fullPath, long totalSamples);
        void SaveTrack(MusicTrack track);
        
        List<Playlist> GetAllPlaylists();
        int AddPlaylist(string name, string folderPath = null, bool isLinked = false);
        void DeletePlaylist(int playlistId);
        IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId);
        
        // 文件夹管理 (Legacy)
        List<string> GetPlaylists(int playlistId); // 这里其实返回的是 FolderPaths
    }

    public class DatabaseHelper : IDatabaseHelper
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseHelper()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "SeamlessLoopMusic");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            _dbPath = Path.Combine(folder, "library.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";
        }

        public IDbConnection GetConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public void InitializeDatabase()
        {
            using (var db = GetConnection())
            {
                // 1. 核心循环点与曲目表
                db.Execute(@"
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
                        UNIQUE(FileName, TotalSamples)
                    );");

                // 2. 歌单定义表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Playlists (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        FolderPath TEXT, 
                        IsFolderLinked INTEGER DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );");

                // 3. 歌单项表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS PlaylistItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PlaylistId INTEGER NOT NULL,
                        SongId INTEGER NOT NULL,
                        SortOrder INTEGER DEFAULT 0,
                        FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                        FOREIGN KEY(SongId) REFERENCES LoopPoints(Id) ON DELETE CASCADE
                    );");

                // 4. 歌单文件夹表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS PlaylistFolders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PlaylistId INTEGER NOT NULL,
                        FolderPath TEXT NOT NULL,
                        AddedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                        UNIQUE(PlaylistId, FolderPath)
                    );");
            }
        }

        // --- 兼容性实现 (转发至 SQL 或 占位) ---

        public IEnumerable<MusicTrack> GetAllTracks() 
        {
            using (var db = GetConnection()) return db.Query<MusicTrack>("SELECT * FROM LoopPoints");
        }

        public MusicTrack GetTrack(string fullPath, long totalSamples)
        {
            using (var db = GetConnection())
            {
                string fileName = Path.GetFileName(fullPath);
                return db.QueryFirstOrDefault<MusicTrack>(
                    "SELECT * FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @Total", 
                    new { FileName = fileName, Total = totalSamples });
            }
        }

        public void SaveTrack(MusicTrack track)
        {
            // 这里简单实现，主要保证编译通过
            using (var db = GetConnection())
            {
                db.Execute(@"UPDATE LoopPoints SET LoopStart=@LoopStart, LoopEnd=@LoopEnd WHERE Id=@Id", track);
            }
        }

        public List<Playlist> GetAllPlaylists()
        {
            using (var db = GetConnection()) return db.Query<Playlist>("SELECT * FROM Playlists").ToList();
        }

        public int AddPlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            using (var db = GetConnection())
            {
                return db.ExecuteScalar<int>(@"
                    INSERT INTO Playlists (Name, FolderPath, IsFolderLinked) 
                    VALUES (@Name, @Path, @Linked);
                    SELECT last_insert_rowid();", 
                    new { Name = name, Path = folderPath, Linked = isLinked ? 1 : 0 });
            }
        }

        public void DeletePlaylist(int playlistId)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM Playlists WHERE Id=@Id", new { Id = playlistId });
        }

        public IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId)
        {
            using (var db = GetConnection())
            {
                return db.Query<MusicTrack>(@"
                    SELECT t.* FROM LoopPoints t
                    JOIN PlaylistItems pi ON t.Id = pi.SongId
                    WHERE pi.PlaylistId = @Id
                    ORDER BY pi.SortOrder", new { Id = playlistId });
            }
        }

        public List<string> GetPlaylists(int playlistId)
        {
            using (var db = GetConnection())
            {
                return db.Query<string>("SELECT FolderPath FROM PlaylistFolders WHERE PlaylistId = @Id", new { Id = playlistId }).ToList();
            }
        }
    }
}
