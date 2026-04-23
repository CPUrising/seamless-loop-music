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
        void BulkInsert(IEnumerable<MusicTrack> tracks);
        
        List<Playlist> GetAllPlaylists();
        int AddPlaylist(string name, string folderPath = null, bool isLinked = false);
        void DeletePlaylist(int playlistId);
        void RenamePlaylist(int playlistId, string newName);
        void BulkSaveTracksAndAddToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId);
        void AddSongToPlaylist(int playlistId, int songId);
        void RemoveSongFromPlaylist(int playlistId, int songId);
        IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId);
        bool IsSongInPlaylist(int playlistId, int songId);
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);
        
        // 存储分析结果与元数据
        void UpdateTrackAnalysis(MusicTrack track);
        
        // 文件夹管理 (Legacy)
        List<string> GetPlaylists(int playlistId); // 这里返回的是 FolderPaths
        void AddPlaylist(int playlistId, string folderPath);
        void RemovePlaylist(int playlistId, string folderPath);

        // 音乐文件夹管理
        List<string> GetMusicFolders();
        void AddMusicFolder(string folderPath);
        void RemoveMusicFolder(string folderPath);
    }

    public class DatabaseHelper : IDatabaseHelper
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseHelper()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string folder = Path.Combine(exeDir, "Data");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            _dbPath = Path.Combine(folder, "LoopData.db");
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
                        Rating INTEGER DEFAULT 0,
                        CoverPath TEXT,
                        UNIQUE(FileName, TotalSamples)
                    );");
                
                // 自动迁移：检查并添加缺失的字段
                var columnsResult = db.Query("PRAGMA table_info(LoopPoints)").Cast<System.Collections.Generic.IDictionary<string, object>>();
                var columnNames = columnsResult.Select(c => c["name"].ToString()).ToList();

                if (!columnNames.Contains("IsLoved"))
                {
                    db.Execute("ALTER TABLE LoopPoints ADD COLUMN IsLoved INTEGER DEFAULT 0;");
                }
                if (!columnNames.Contains("Rating"))
                {
                    db.Execute("ALTER TABLE LoopPoints ADD COLUMN Rating INTEGER DEFAULT 0;");
                }
                if (!columnNames.Contains("CoverPath"))
                {
                    db.Execute("ALTER TABLE LoopPoints ADD COLUMN CoverPath TEXT;");
                }
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Playlists (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        FolderPath TEXT, 
                        IsFolderLinked INTEGER DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );");
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS PlaylistItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PlaylistId INTEGER NOT NULL,
                        SongId INTEGER NOT NULL,
                        SortOrder INTEGER DEFAULT 0,
                        FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                        FOREIGN KEY(SongId) REFERENCES LoopPoints(Id) ON DELETE CASCADE
                    );");
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS PlaylistFolders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PlaylistId INTEGER NOT NULL,
                        FolderPath TEXT NOT NULL,
                        AddedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                        UNIQUE(PlaylistId, FolderPath)
                    );");
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS MusicFolders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FolderPath TEXT NOT NULL UNIQUE,
                        AddedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );");
            }
        }

        public IEnumerable<MusicTrack> GetAllTracks() 
            => GetConnection().Query<MusicTrack>("SELECT * FROM LoopPoints");

        public MusicTrack GetTrack(string fullPath, long totalSamples)
        {
            string fileName = Path.GetFileName(fullPath);
            return GetConnection().QueryFirstOrDefault<MusicTrack>(
                "SELECT * FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @Total", 
                new { FileName = fileName, Total = totalSamples });
        }

        public void SaveTrack(MusicTrack track)
        {
            using (var db = GetConnection()) db.Execute(@"UPDATE LoopPoints SET LoopStart=@LoopStart, LoopEnd=@LoopEnd WHERE Id=@Id", track);
        }

        public void UpdateTrackAnalysis(MusicTrack track)
        {
            using (var db = GetConnection())
            {
                db.Execute(@"
                    UPDATE LoopPoints SET 
                        Artist = @Artist, 
                        Album = @Album, 
                        AlbumArtist = @AlbumArtist, 
                        DisplayName = @DisplayName,
                        LoopStart = @LoopStart,
                        LoopEnd = @LoopEnd,
                        LoopCandidatesJson = @LoopCandidatesJson,
                        CoverPath = @CoverPath
                    WHERE Id = @Id", track);
            }
        }

        public void BulkInsert(IEnumerable<MusicTrack> tracks)
        {
            using (var db = GetConnection())
            {
                using (var trans = db.BeginTransaction())
                {
                    db.Execute(@"
                        INSERT INTO LoopPoints 
                        (FileName, FilePath, DisplayName, TotalSamples, LoopStart, LoopEnd, Artist, Album, AlbumArtist, LastModified, LoopCandidatesJson, CoverPath) 
                        VALUES 
                        (@FileName, @FilePath, @DisplayName, @TotalSamples, @LoopStart, @LoopEnd, @Artist, @Album, @AlbumArtist, @LastModified, @LoopCandidatesJson, @CoverPath)
                        ON CONFLICT(FileName, TotalSamples) DO UPDATE SET
                            FilePath = excluded.FilePath,
                            DisplayName = excluded.DisplayName,
                            Artist = excluded.Artist,
                            Album = excluded.Album,
                            AlbumArtist = excluded.AlbumArtist,
                            CoverPath = excluded.CoverPath,
                            LastModified = excluded.LastModified;", 
                        tracks, transaction: trans);
                    trans.Commit();
                }
            }
        }

        public List<Playlist> GetAllPlaylists()
            => GetConnection().Query<Playlist>("SELECT * FROM Playlists ORDER BY SortOrder").ToList();

        public int AddPlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            using (var db = GetConnection()) return db.ExecuteScalar<int>(@"INSERT INTO Playlists (Name, FolderPath, IsFolderLinked) VALUES (@Name, @Path, @Linked); SELECT last_insert_rowid();", new { Name = name, Path = folderPath, Linked = isLinked ? 1 : 0 });
        }

        public void DeletePlaylist(int playlistId)
            => GetConnection().Execute("DELETE FROM Playlists WHERE Id=@Id", new { Id = playlistId });

        public void RenamePlaylist(int playlistId, string newName)
            => GetConnection().Execute("UPDATE Playlists SET Name=@Name WHERE Id=@Id", new { Name = newName, Id = playlistId });

        public void BulkSaveTracksAndAddToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId)
        {
            // Simplified for compatibility
            foreach(var t in tracks) AddSongToPlaylist(playlistId, t.Id);
        }

        public void AddSongToPlaylist(int playlistId, int songId)
            => GetConnection().Execute("INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId) VALUES (@Pid, @Sid)", new { Pid = playlistId, Sid = songId });

        public void RemoveSongFromPlaylist(int playlistId, int songId)
            => GetConnection().Execute("DELETE FROM PlaylistItems WHERE PlaylistId=@Pid AND SongId=@Sid", new { Pid = playlistId, Sid = songId });

        public IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId)
            => GetConnection().Query<MusicTrack>(@"SELECT t.* FROM LoopPoints t JOIN PlaylistItems pi ON t.Id = pi.SongId WHERE pi.PlaylistId = @Id ORDER BY pi.SortOrder", new { Id = playlistId });

        public bool IsSongInPlaylist(int playlistId, int songId)
            => GetConnection().ExecuteScalar<bool>("SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId=@Pid AND SongId=@Sid", new { Pid = playlistId, Sid = songId });

        public void UpdatePlaylistsSortOrder(List<int> playlistIds) { }
        public void UpdateTracksSortOrder(int playlistId, List<int> songIds) { }

        public List<string> GetPlaylists(int playlistId)
            => GetConnection().Query<string>("SELECT FolderPath FROM PlaylistFolders WHERE PlaylistId = @Id", new { Id = playlistId }).ToList();

        public void AddPlaylist(int playlistId, string folderPath)
            => GetConnection().Execute("INSERT OR IGNORE INTO PlaylistFolders (PlaylistId, FolderPath) VALUES (@Pid, @Path)", new { Pid = playlistId, Path = folderPath });

        public void RemovePlaylist(int playlistId, string folderPath)
            => GetConnection().Execute("DELETE FROM PlaylistFolders WHERE PlaylistId=@Pid AND FolderPath=@Path", new { Pid = playlistId, Path = folderPath });

        public List<string> GetMusicFolders()
            => GetConnection().Query<string>("SELECT FolderPath FROM MusicFolders ORDER BY AddedAt").ToList();

        public void AddMusicFolder(string folderPath)
            => GetConnection().Execute("INSERT OR IGNORE INTO MusicFolders (FolderPath) VALUES (@Path)", new { Path = folderPath });

        public void RemoveMusicFolder(string folderPath)
            => GetConnection().Execute("DELETE FROM MusicFolders WHERE FolderPath=@Path", new { Path = folderPath });
    }
}
