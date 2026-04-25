using System;
using System.IO;
using System.Data;
using System.Data.SQLite;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        // 数据库同步
        (int tracksSynced, int playlistsSynced) SyncWithExternalDatabase(string externalDbPath);
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
            _connectionString = $"Data Source={_dbPath};Version=3;Busy Timeout=5000;";
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
                // 开启 WAL 模式以支持并发读写
                db.Execute("PRAGMA journal_mode=WAL;");
                db.Execute("PRAGMA synchronous=NORMAL;");
                db.Execute("PRAGMA foreign_keys=ON;");

                // 1. Artists 表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Artists (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        CoverPath TEXT
                    );");

                // 2. Albums 表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Albums (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        ArtistId INTEGER,
                        CoverPath TEXT,
                        FOREIGN KEY(ArtistId) REFERENCES Artists(Id) ON DELETE SET NULL,
                        UNIQUE(Name, ArtistId)
                    );");

                // 3. Tracks 表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Tracks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FileName TEXT NOT NULL,
                        FilePath TEXT,
                        DisplayName TEXT,
                        TotalSamples INTEGER DEFAULT 0,
                        LastModified DATETIME,
                        CoverPath TEXT,
                        AlbumId INTEGER,
                        FOREIGN KEY(AlbumId) REFERENCES Albums(Id) ON DELETE SET NULL,
                        UNIQUE(FileName, TotalSamples)
                    );");

                // 4. LoopPoints 表 (新版，与 Tracks 1:1)
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS LoopPoints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TrackId INTEGER NOT NULL UNIQUE,
                        LoopStart INTEGER DEFAULT 0,
                        LoopEnd INTEGER DEFAULT 0,
                        LoopCandidatesJson TEXT,
                        AnalysisLastModified DATETIME,
                        FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
                    );");

                // 5. UserRatings 表
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS UserRatings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TrackId INTEGER NOT NULL UNIQUE,
                        Rating INTEGER DEFAULT 0,
                        IsLoved INTEGER DEFAULT 0,
                        LastModified DATETIME,
                        FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
                    );");

                // 6. Playlists 相关表
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
                        FOREIGN KEY(SongId) REFERENCES Tracks(Id) ON DELETE CASCADE
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

        private const string FullTrackSelect = @"
            SELECT
                t.Id, t.FileName, t.FilePath, t.DisplayName, t.TotalSamples, t.LastModified, t.CoverPath,
                al.Name AS Album, ar.Name AS Artist, ar.Name AS AlbumArtist,
                COALESCE(lp.LoopStart, 0) AS LoopStart, COALESCE(lp.LoopEnd, 0) AS LoopEnd,
                lp.LoopCandidatesJson,
                COALESCE(ur.IsLoved, 0) AS IsLoved, COALESCE(ur.Rating, 0) AS Rating
            FROM Tracks t
            LEFT JOIN Albums al ON t.AlbumId = al.Id
            LEFT JOIN Artists ar ON al.ArtistId = ar.Id
            LEFT JOIN LoopPoints lp ON t.Id = lp.TrackId
            LEFT JOIN UserRatings ur ON t.Id = ur.TrackId";

        public IEnumerable<MusicTrack> GetAllTracks() 
            => GetConnection().Query<MusicTrack>(FullTrackSelect);

        public MusicTrack GetTrack(string fullPath, long totalSamples)
        {
            string fileName = Path.GetFileName(fullPath);
            return GetConnection().QueryFirstOrDefault<MusicTrack>(
                FullTrackSelect + " WHERE t.FileName = @FileName AND t.TotalSamples = @Total", 
                new { FileName = fileName, Total = totalSamples });
        }

        public void SaveTrack(MusicTrack track)
        {
            using (var db = GetConnection()) 
            {
                db.Execute(@"
                    INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd) 
                    VALUES (@Id, @LoopStart, @LoopEnd)
                    ON CONFLICT(TrackId) DO UPDATE SET 
                        LoopStart = excluded.LoopStart, 
                        LoopEnd = excluded.LoopEnd", track);
            }
        }

        public void UpdateTrackAnalysis(MusicTrack track)
        {
            using (var db = GetConnection())
            {
                using (var trans = db.BeginTransaction())
                {
                    int? albumId = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);
                    
                    db.Execute(@"
                        UPDATE Tracks SET 
                            DisplayName = @DisplayName,
                            CoverPath = @CoverPath,
                            AlbumId = @AlbumId
                        WHERE Id = @Id", new { track.DisplayName, track.CoverPath, AlbumId = albumId, track.Id }, transaction: trans);

                    db.Execute(@"
                        INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                        VALUES (@Id, @LoopStart, @LoopEnd, @LoopCandidatesJson, @Now)
                        ON CONFLICT(TrackId) DO UPDATE SET
                            LoopStart = excluded.LoopStart,
                            LoopEnd = excluded.LoopEnd,
                            LoopCandidatesJson = excluded.LoopCandidatesJson,
                            AnalysisLastModified = excluded.AnalysisLastModified", 
                        new { track.Id, track.LoopStart, track.LoopEnd, track.LoopCandidatesJson, Now = DateTime.Now }, transaction: trans);
                    
                    trans.Commit();
                }
            }
        }

        public void BulkInsert(IEnumerable<MusicTrack> tracks)
        {
            using (var db = GetConnection())
            {
                using (var trans = db.BeginTransaction())
                {
                    foreach (var track in tracks)
                    {
                        int? albumId = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);

                        long trackId = db.ExecuteScalar<long>(@"
                            INSERT INTO Tracks 
                            (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId) 
                            VALUES 
                            (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId)
                            ON CONFLICT(FileName, TotalSamples) DO UPDATE SET
                                FilePath = excluded.FilePath,
                                DisplayName = excluded.DisplayName,
                                CoverPath = excluded.CoverPath,
                                LastModified = excluded.LastModified,
                                AlbumId = excluded.AlbumId;
                            SELECT Id FROM Tracks WHERE FileName = @FileName AND TotalSamples = @TotalSamples;", 
                            new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId }, 
                            transaction: trans);

                        db.Execute(@"
                            INSERT OR IGNORE INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                            VALUES (@TrackId, @LoopStart, @LoopEnd, @Json, @Now)",
                            new { TrackId = trackId, track.LoopStart, track.LoopEnd, Json = track.LoopCandidatesJson, Now = track.LastModified },
                            transaction: trans);

                        db.Execute(@"
                            INSERT OR IGNORE INTO UserRatings (TrackId, Rating, IsLoved, LastModified)
                            VALUES (@TrackId, @Rating, @IsLoved, @Now)",
                            new { TrackId = trackId, track.Rating, IsLoved = track.IsLoved ? 1 : 0, Now = track.LastModified },
                            transaction: trans);
                    }
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
            => GetConnection().Query<MusicTrack>(FullTrackSelect + @" JOIN PlaylistItems pi ON t.Id = pi.SongId WHERE pi.PlaylistId = @Id ORDER BY pi.SortOrder", new { Id = playlistId });

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

        private class SyncSongDto
        {
            public string FileName { get; set; }
            public long TotalSamples { get; set; }
        }

        public (int tracksSynced, int playlistsSynced) SyncWithExternalDatabase(string externalDbPath)
        {
            int tracksSynced = 0;
            int playlistsSynced = 0;

            using (var db = GetConnection())
            {
                db.Execute($"ATTACH DATABASE '{externalDbPath}' AS ExternalDB");
                try
                {
                    // 1. 获取外部表信息，处理可能缺失的列
                    var columns = db.Query<string>("PRAGMA ExternalDB.table_info(LoopPoints)").Select(c => c.ToString()).ToList();
                    bool hasCandidates = columns.Contains("LoopCandidatesJson");
                    bool hasRating = columns.Contains("Rating");
                    bool hasIsLoved = columns.Contains("IsLoved");
                    bool hasCover = columns.Contains("CoverPath");
                    bool hasDisplayName = columns.Contains("DisplayName");

                    // 2. 同步曲目信息
                    // 采用“模糊匹配”：文件名一致 + 采样数误差极小 (10000 采样以内)
                    string syncSql = $@"
                        SELECT 
                            FileName, TotalSamples, 
                            {(hasDisplayName ? "DisplayName" : "FileName AS DisplayName")},
                            Artist, Album, AlbumArtist, LoopStart, LoopEnd,
                            {(hasCandidates ? "LoopCandidatesJson" : "NULL AS LoopCandidatesJson")},
                            {(hasRating ? "Rating" : "0 AS Rating")},
                            {(hasIsLoved ? "IsLoved" : "0 AS IsLoved")},
                            {(hasCover ? "CoverPath" : "NULL AS CoverPath")}
                        FROM ExternalDB.LoopPoints";

                    var externalTracks = db.Query<MusicTrack>(syncSql).ToList();
                    
                    foreach (var ext in externalTracks)
                    {
                        // 在本地库寻找匹配的 Track
                        var localTrack = db.QueryFirstOrDefault<MusicTrack>(
                            "SELECT Id FROM Tracks WHERE LOWER(TRIM(FileName)) = LOWER(TRIM(@FileName)) AND ABS(TotalSamples - @TotalSamples) < 10000",
                            new { ext.FileName, ext.TotalSamples });

                        if (localTrack != null)
                        {
                            using (var trans = db.BeginTransaction())
                            {
                                int? albumId = UpsertArtistAlbum(db, ext.Artist, ext.AlbumArtist, ext.Album, ext.CoverPath, trans);
                                
                                db.Execute(@"
                                    UPDATE Tracks SET 
                                        DisplayName = @DisplayName,
                                        AlbumId = @AlbumId,
                                        CoverPath = COALESCE(@CoverPath, CoverPath)
                                    WHERE Id = @Id", new { ext.DisplayName, AlbumId = albumId, ext.CoverPath, Id = localTrack.Id }, transaction: trans);

                                db.Execute(@"
                                    INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                                    VALUES (@Id, @LoopStart, @LoopEnd, @Json, @Now)
                                    ON CONFLICT(TrackId) DO UPDATE SET
                                        LoopStart = excluded.LoopStart,
                                        LoopEnd = excluded.LoopEnd,
                                        LoopCandidatesJson = COALESCE(excluded.LoopCandidatesJson, LoopCandidatesJson),
                                        AnalysisLastModified = excluded.AnalysisLastModified",
                                    new { localTrack.Id, ext.LoopStart, ext.LoopEnd, Json = ext.LoopCandidatesJson, Now = DateTime.Now }, transaction: trans);

                                db.Execute(@"
                                    INSERT INTO UserRatings (TrackId, Rating, IsLoved, LastModified)
                                    VALUES (@Id, @Rating, @IsLoved, @Now)
                                    ON CONFLICT(TrackId) DO UPDATE SET
                                        Rating = excluded.Rating,
                                        IsLoved = excluded.IsLoved,
                                        LastModified = excluded.LastModified",
                                    new { localTrack.Id, ext.Rating, ext.IsLoved, Now = DateTime.Now }, transaction: trans);

                                trans.Commit();
                                tracksSynced++;
                            }
                        }
                    }

                    // 3. 同步歌单
                    var externalPlaylists = db.Query<Playlist>("SELECT * FROM ExternalDB.Playlists").ToList();
                    foreach (var extPl in externalPlaylists)
                    {
                        int? localPlId = db.ExecuteScalar<int?>(@"SELECT Id FROM Playlists WHERE Name = @Name LIMIT 1", new { Name = extPl.Name });
                        if (!localPlId.HasValue)
                        {
                            localPlId = db.ExecuteScalar<int>(@"
                                INSERT INTO Playlists (Name, FolderPath, IsFolderLinked, SortOrder) 
                                VALUES (@Name, @FolderPath, @IsFolderLinked, @SortOrder);
                                SELECT last_insert_rowid();", 
                                new { Name = extPl.Name, FolderPath = extPl.FolderPath, IsFolderLinked = extPl.IsFolderLinked ? 1 : 0, SortOrder = extPl.SortOrder });
                        }

                        var extSongs = db.Query<SyncSongDto>(@"
                            SELECT lp.FileName, lp.TotalSamples 
                            FROM ExternalDB.PlaylistItems pi
                            JOIN ExternalDB.LoopPoints lp ON pi.SongId = lp.Id
                            WHERE pi.PlaylistId = @ExtPlId", new { ExtPlId = extPl.Id }).ToList();

                        foreach (var song in extSongs)
                        {
                            int? localSongId = db.ExecuteScalar<int?>(@"
                                SELECT Id FROM Tracks 
                                WHERE LOWER(TRIM(FileName)) = LOWER(TRIM(@FileName)) 
                                  AND ABS(TotalSamples - @TotalSamples) < 10000 
                                LIMIT 1", 
                                new { song.FileName, song.TotalSamples });

                            if (localSongId.HasValue)
                            {
                                db.Execute(@"
                                    INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId) 
                                    VALUES (@PlId, @SongId)", new { PlId = localPlId.Value, SongId = localSongId.Value });
                            }
                        }
                        playlistsSynced++;
                    }
                }
                finally
                {
                    db.Execute("DETACH DATABASE ExternalDB");
                }
            }

            return (tracksSynced, playlistsSynced);
        }

        private int? UpsertArtistAlbum(IDbConnection db, string artist, string albumArtist, string album, string coverPath, IDbTransaction trans)
        {
            if (string.IsNullOrWhiteSpace(album)) return null;
            string artistName = !string.IsNullOrWhiteSpace(albumArtist) ? albumArtist : artist;
            int? artistId = null;
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                db.Execute("INSERT OR IGNORE INTO Artists (Name) VALUES (@Name)", new { Name = artistName }, transaction: trans);
                artistId = db.ExecuteScalar<int>("SELECT Id FROM Artists WHERE Name = @Name", new { Name = artistName }, transaction: trans);
            }

            db.Execute("INSERT OR IGNORE INTO Albums (Name, ArtistId, CoverPath) VALUES (@Name, @ArtistId, @CoverPath)", 
                new { Name = album, ArtistId = artistId, CoverPath = coverPath }, transaction: trans);
            
            return db.ExecuteScalar<int?>("SELECT Id FROM Albums WHERE Name = @Name AND (ArtistId = @ArtistId OR (ArtistId IS NULL AND @ArtistId IS NULL))", 
                new { Name = album, ArtistId = artistId }, transaction: trans);
        }
    }
}
