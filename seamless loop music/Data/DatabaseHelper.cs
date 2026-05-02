using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data
{
    public interface IDatabaseHelper
    {
        IDbConnection GetConnection();
        void InitializeDatabase();
        IEnumerable<MusicTrack> GetAllTracks();
        MusicTrack GetTrack(string fullPath, long totalSamples);
        void SaveTrack(MusicTrack track);
        void UpdateTrackAnalysis(MusicTrack track);
        void BulkInsert(IEnumerable<MusicTrack> tracks);
        
        // Playlist related
        List<Playlist> GetAllPlaylists();
        int AddPlaylist(string name);
        void DeletePlaylist(int playlistId);
        void RenamePlaylist(int playlistId, string newName);
        void BulkSaveTracksAndAddToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId);
        void AddSongToPlaylist(int playlistId, int songId);
        void RemoveSongFromPlaylist(int playlistId, int songId);
        IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId);
        bool IsSongInPlaylist(int playlistId, int songId);
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);
        
        // Folder related
        List<string> GetMusicFolders();
        void AddMusicFolder(string folderPath);
        void RemoveMusicFolder(string folderPath);
        
        
        // Settings
        string GetSetting(string key, string defaultValue = null);
        void SetSetting(string key, string value);
        
        (int tracksSynced, int playlistsSynced) SyncWithExternalDatabase(string externalDbPath);
        int CleanupMissingFiles();
    }

    public class DatabaseHelper : IDatabaseHelper
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseHelper() : this(null) { }

        public DatabaseHelper(string customDbPath = null)
        {
            if (string.IsNullOrEmpty(customDbPath))
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string folder = Path.Combine(exeDir, "Data");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                _dbPath = Path.Combine(folder, "LoopData.db");
            }
            else
            {
                if (customDbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                {
                    _connectionString = customDbPath;
                    _dbPath = customDbPath; // 内存模式下路径意义不大
                    return;
                }
                _dbPath = customDbPath;
            }
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
                db.Execute("PRAGMA journal_mode=WAL;");
                db.Execute("PRAGMA synchronous=NORMAL;");
                db.Execute("PRAGMA foreign_keys=ON;");

                // --- 纯净 3NF 架构创建 ---
                db.Execute(@"CREATE TABLE IF NOT EXISTS Artists (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, CoverPath TEXT);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS Albums (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ArtistId INTEGER, CoverPath TEXT, FOREIGN KEY(ArtistId) REFERENCES Artists(Id) ON DELETE SET NULL, UNIQUE(Name, ArtistId));");
                db.Execute(@"CREATE TABLE IF NOT EXISTS Tracks (Id INTEGER PRIMARY KEY AUTOINCREMENT, FileName TEXT NOT NULL, FilePath TEXT, DisplayName TEXT, TotalSamples INTEGER DEFAULT 0, LastModified DATETIME, CoverPath TEXT, AlbumId INTEGER, FOREIGN KEY(AlbumId) REFERENCES Albums(Id) ON DELETE SET NULL, UNIQUE(FileName, TotalSamples));");
                db.Execute(@"CREATE TABLE IF NOT EXISTS LoopPoints (TrackId INTEGER PRIMARY KEY, LoopStart INTEGER DEFAULT 0, LoopEnd INTEGER DEFAULT 0, LoopCandidatesJson TEXT, AnalysisLastModified DATETIME, FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS UserRatings (TrackId INTEGER PRIMARY KEY, Rating INTEGER DEFAULT 0, LastModified DATETIME, FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS Playlists (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, SortOrder INTEGER DEFAULT 0, CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS PlaylistItems (PlaylistId INTEGER, SongId INTEGER, SortOrder INTEGER DEFAULT 0, PRIMARY KEY(PlaylistId, SongId), FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE, FOREIGN KEY(SongId) REFERENCES Tracks(Id) ON DELETE CASCADE);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS MusicFolders (Id INTEGER PRIMARY KEY AUTOINCREMENT, FolderPath TEXT NOT NULL UNIQUE, AddedAt DATETIME DEFAULT CURRENT_TIMESTAMP);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS AppSettings (Key TEXT PRIMARY KEY, Value TEXT);");

                // 执行迁移（加约束、查重等）
                ApplyMigrations(db);

                // --- 性能索引优化 ---
                db.Execute("CREATE INDEX IF NOT EXISTS idx_tracks_albumid ON Tracks(AlbumId);");
                db.Execute("CREATE INDEX IF NOT EXISTS idx_albums_artistid ON Albums(ArtistId);");
                db.Execute("CREATE INDEX IF NOT EXISTS idx_playlistitems_songid ON PlaylistItems(SongId);");
            }
        }

        private const string FullTrackSelect = @"
            SELECT 
                t.Id, t.FileName, t.FilePath, t.DisplayName, t.TotalSamples, t.LastModified, t.CoverPath,
                al.Name AS Album, ar.Name AS Artist, ar.Name AS AlbumArtist, 
                al.CoverPath AS AlbumCoverPath, ar.CoverPath AS ArtistCoverPath,
                COALESCE(lp.LoopStart, 0) AS LoopStart, COALESCE(lp.LoopEnd, 0) AS LoopEnd,
                lp.LoopCandidatesJson,
                COALESCE(ur.Rating, 0) AS Rating
            FROM Tracks t
            LEFT JOIN Albums al ON t.AlbumId = al.Id
            LEFT JOIN Artists ar ON al.ArtistId = ar.Id
            LEFT JOIN LoopPoints lp ON t.Id = lp.TrackId
            LEFT JOIN UserRatings ur ON t.Id = ur.TrackId
        ";

        public IEnumerable<MusicTrack> GetAllTracks()
        {
            using (var db = GetConnection()) return db.Query<MusicTrack>(FullTrackSelect).ToList();
        }

        public MusicTrack GetTrack(string fullPath, long totalSamples)
        {
            string fileName = Path.GetFileName(fullPath);
            using (var db = GetConnection()) return db.QueryFirstOrDefault<MusicTrack>(
                FullTrackSelect + " WHERE t.FileName = @FileName AND t.TotalSamples = @Total", 
                new { FileName = fileName, Total = totalSamples });
        }

        public void SaveTrack(MusicTrack track)
        {
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                int? albumId = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);
                
                db.Execute(@"UPDATE Tracks SET DisplayName=@DisplayName, CoverPath=@CoverPath, AlbumId=@AlbumId WHERE Id=@Id", 
                    new { track.DisplayName, track.CoverPath, AlbumId = albumId, track.Id }, transaction: trans);

                db.Execute(@"INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson) VALUES (@Id, @LoopStart, @LoopEnd, @LoopCandidatesJson) ON CONFLICT(TrackId) DO UPDATE SET LoopStart=excluded.LoopStart, LoopEnd=excluded.LoopEnd, LoopCandidatesJson=excluded.LoopCandidatesJson", 
                    track, transaction: trans);

                db.Execute(@"INSERT INTO UserRatings (TrackId, Rating, LastModified) VALUES (@Id, @Rating, @LastModified) ON CONFLICT(TrackId) DO UPDATE SET Rating=excluded.Rating, LastModified=excluded.LastModified", 
                    track, transaction: trans);
                
                trans.Commit();
            }
        }

        private int? UpsertArtistAlbum(IDbConnection db, string artist, string albumArtist, string album, string coverPath, IDbTransaction trans)
        {
            string artistName = !string.IsNullOrEmpty(artist) ? artist : (!string.IsNullOrEmpty(albumArtist) ? albumArtist : "Unknown Artist");
            string albumName = !string.IsNullOrEmpty(album) ? album : "Unknown Album";
            
            db.Execute("INSERT OR IGNORE INTO Artists (Name, CoverPath) VALUES (@Name, NULL)", new { Name = artistName }, transaction: trans);
            int artistId = db.ExecuteScalar<int>("SELECT Id FROM Artists WHERE Name = @Name", new { Name = artistName }, transaction: trans);

            // 若传入的 coverPath 有效，且 Artist 当前无封面，则更新
            if (!string.IsNullOrEmpty(coverPath))
            {
                db.Execute(@"UPDATE Artists 
                            SET CoverPath = @Cover 
                            WHERE Name = @Name AND (CoverPath IS NULL OR CoverPath = '')", 
                            new { Name = artistName, Cover = coverPath }, transaction: trans);
            }

            // Album: 升级为 ON CONFLICT 模式，如果发现新封面则自动补全
            db.Execute(@"
                INSERT INTO Albums (Name, ArtistId, CoverPath)
                VALUES (@Name, @ArtistId, @Cover)
                ON CONFLICT(Name, ArtistId) DO UPDATE SET
                    CoverPath = excluded.CoverPath
                WHERE Albums.CoverPath IS NULL OR Albums.CoverPath = '';", 
                new { Name = albumName, ArtistId = artistId, Cover = coverPath }, transaction: trans);
            return db.ExecuteScalar<int>("SELECT Id FROM Albums WHERE Name = @Name AND ArtistId = @ArtistId", 
                new { Name = albumName, ArtistId = artistId }, transaction: trans);
        }

        public void UpdateTrackAnalysis(MusicTrack track) => SaveTrack(track);

        public void BulkInsert(IEnumerable<MusicTrack> tracks)
        {
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                foreach (var track in tracks)
                {
                    int? albumId = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);
                    
                    long trackId = db.ExecuteScalar<long>(@"
                        INSERT INTO Tracks (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId) 
                        VALUES (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId)
                        ON CONFLICT(FileName, TotalSamples) DO UPDATE SET FilePath=excluded.FilePath, DisplayName=excluded.DisplayName, AlbumId=excluded.AlbumId;
                        SELECT Id FROM Tracks WHERE FileName=@FileName AND TotalSamples=@TotalSamples;", 
                        new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId }, transaction: trans);

                    track.Id = (int)trackId;
                    db.Execute(@"INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson) VALUES (@TrackId, @LoopStart, @LoopEnd, @LoopCandidatesJson) ON CONFLICT(TrackId) DO UPDATE SET LoopStart=excluded.LoopStart, LoopEnd=excluded.LoopEnd, LoopCandidatesJson=excluded.LoopCandidatesJson", 
                        new { TrackId = trackId, track.LoopStart, track.LoopEnd, track.LoopCandidatesJson }, transaction: trans);
                    db.Execute(@"INSERT INTO UserRatings (TrackId, Rating, LastModified) VALUES (@TrackId, @Rating, @Now) ON CONFLICT(TrackId) DO UPDATE SET Rating=excluded.Rating, LastModified = excluded.LastModified;",
                        new { TrackId = trackId, track.Rating, Now = track.LastModified },
                        transaction: trans);
                }
                trans.Commit();
            }
        }

        public List<Playlist> GetAllPlaylists()
        {
            using (var db = GetConnection()) return db.Query<Playlist>("SELECT * FROM Playlists ORDER BY SortOrder").ToList();
        }

        public int AddPlaylist(string name)
        {
            using (var db = GetConnection()) return db.ExecuteScalar<int>(@"INSERT INTO Playlists (Name) VALUES (@Name); SELECT last_insert_rowid();", new { Name = name });
        }

        public void DeletePlaylist(int playlistId)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM Playlists WHERE Id=@Id", new { Id = playlistId });
        }

        public void RenamePlaylist(int playlistId, string newName)
        {
            using (var db = GetConnection()) db.Execute("UPDATE Playlists SET Name=@Name WHERE Id=@Id", new { Name = newName, Id = playlistId });
        }

        public void BulkSaveTracksAndAddToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId) { BulkInsert(tracks); foreach(var t in tracks) AddSongToPlaylist(playlistId, t.Id); }
        
        public void AddSongToPlaylist(int playlistId, int songId)
        {
            using (var db = GetConnection()) db.Execute("INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId) VALUES (@Pid, @Sid)", new { Pid = playlistId, Sid = songId });
        }

        public void RemoveSongFromPlaylist(int playlistId, int songId)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM PlaylistItems WHERE PlaylistId=@Pid AND SongId=@Sid", new { Pid = playlistId, Sid = songId });
        }

        public IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId)
        {
            using (var db = GetConnection()) return db.Query<MusicTrack>(FullTrackSelect + " JOIN PlaylistItems pi ON t.Id = pi.SongId WHERE pi.PlaylistId = @Id ORDER BY pi.SortOrder", new { Id = playlistId }).ToList();
        }

        public bool IsSongInPlaylist(int playlistId, int songId)
        {
            using (var db = GetConnection()) return db.ExecuteScalar<bool>("SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId=@Pid AND SongId=@Sid", new { Pid = playlistId, Sid = songId });
        }

        public void UpdatePlaylistsSortOrder(List<int> playlistIds)
        {
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                for (int i = 0; i < playlistIds.Count; i++)
                {
                    db.Execute("UPDATE Playlists SET SortOrder = @Order WHERE Id = @Id", 
                        new { Order = i, Id = playlistIds[i] }, transaction: trans);
                }
                trans.Commit();
            }
        }

        public void UpdateTracksSortOrder(int playlistId, List<int> songIds)
        {
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                for (int i = 0; i < songIds.Count; i++)
                {
                    db.Execute("UPDATE PlaylistItems SET SortOrder = @Order WHERE PlaylistId = @Pid AND SongId = @Sid", 
                        new { Order = i, Pid = playlistId, Sid = songIds[i] }, transaction: trans);
                }
                trans.Commit();
            }
        }
        
        public List<string> GetMusicFolders()
        {
            using (var db = GetConnection()) return db.Query<string>("SELECT FolderPath FROM MusicFolders ORDER BY AddedAt").ToList();
        }

        public void AddMusicFolder(string folderPath)
        {
            using (var db = GetConnection()) db.Execute("INSERT OR IGNORE INTO MusicFolders (FolderPath) VALUES (@Path)", new { Path = folderPath });
        }

        public void RemoveMusicFolder(string folderPath)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM MusicFolders WHERE FolderPath=@Path", new { Path = folderPath });
        }

        public string GetSetting(string key, string defaultValue = null)
        {
            using (var db = GetConnection())
            {
                return db.QueryFirstOrDefault<string>("SELECT Value FROM AppSettings WHERE Key = @Key", new { Key = key }) ?? defaultValue;
            }
        }

        public void SetSetting(string key, string value)
        {
            using (var db = GetConnection())
            {
                db.Execute("INSERT INTO AppSettings (Key, Value) VALUES (@Key, @Value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value", 
                    new { Key = key, Value = value });
            }
        }

        private class PlaylistDto { public int Id { get; set; } public string Name { get; set; } }

        public (int tracksSynced, int playlistsSynced) SyncWithExternalDatabase(string externalDbPath)
        {
            int tracksSynced = 0;
            int playlistsSynced = 0;
            using (var db = GetConnection())
            {
                db.Execute($"ATTACH DATABASE '{externalDbPath}' AS ExternalDB");
                try
                {
                    // --- 1. 架构自适应探测 ---
                    var tables = db.Query<string>("SELECT name FROM ExternalDB.sqlite_master WHERE type='table'").ToList();
                    bool is3NF = tables.Contains("Tracks", StringComparer.OrdinalIgnoreCase);
                    
                    List<MusicTrack> externalTracks;
                    if (is3NF)
                    {
                        // 探测 Tracks 表字段
                        var trackCols = db.Query("PRAGMA ExternalDB.table_info(Tracks)").Cast<IDictionary<string, object>>()
                                          .Select(c => c["name"].ToString()).ToList();
                        var trackColSet = new HashSet<string>(trackCols, StringComparer.OrdinalIgnoreCase);
                        
                        bool hasDisplayName = trackColSet.Contains("DisplayName");
                        bool hasCover = trackColSet.Contains("CoverPath");

                        bool hasArtists = tables.Contains("Artists", StringComparer.OrdinalIgnoreCase);
                        bool hasAlbums = tables.Contains("Albums", StringComparer.OrdinalIgnoreCase);
                        bool hasLoopPoints = tables.Contains("LoopPoints", StringComparer.OrdinalIgnoreCase);
                        bool hasUserRatings = tables.Contains("UserRatings", StringComparer.OrdinalIgnoreCase);

                        string syncSql3NF = $@"
                            SELECT 
                                t.FileName, t.TotalSamples, 
                                {(hasDisplayName ? "t.DisplayName" : "t.FileName AS DisplayName")}, 
                                {(hasArtists ? "ar.Name" : "NULL")} AS Artist, 
                                {(hasAlbums ? "al.Name" : "NULL")} AS Album, 
                                {(hasArtists ? "ar.Name" : "NULL")} AS AlbumArtist,
                                {(hasLoopPoints ? "lp.LoopStart" : "0")} AS LoopStart, 
                                {(hasLoopPoints ? "lp.LoopEnd" : "0")} AS LoopEnd, 
                                {(hasLoopPoints ? "lp.LoopCandidatesJson" : "NULL")} AS LoopCandidatesJson,
                                {(hasUserRatings ? "ur.Rating" : "0")} AS Rating, 
                                {(hasCover ? "t.CoverPath" : "NULL AS CoverPath")}
                            FROM ExternalDB.Tracks t
                            {(hasAlbums ? "LEFT JOIN ExternalDB.Albums al ON t.AlbumId = al.Id" : "")}
                            {(hasArtists ? "LEFT JOIN ExternalDB.Artists ar ON al.ArtistId = ar.Id" : "")}
                            {(hasLoopPoints ? "LEFT JOIN ExternalDB.LoopPoints lp ON t.Id = lp.TrackId" : "")}
                            {(hasUserRatings ? "LEFT JOIN ExternalDB.UserRatings ur ON t.Id = ur.TrackId" : "")}";
                        externalTracks = db.Query<MusicTrack>(syncSql3NF).ToList();
                    }
                    else if (tables.Contains("LoopPoints", StringComparer.OrdinalIgnoreCase))
                    {
                        // 外部库是旧版 Flat 架构
                        var columnsResult = db.Query("PRAGMA ExternalDB.table_info(LoopPoints)").Cast<IDictionary<string, object>>();
                        var columns = columnsResult.Select(c => c["name"].ToString()).ToList();
                        var colSet = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
                        
                        bool hasDisplayName = colSet.Contains("DisplayName");
                        bool hasCandidates = colSet.Contains("LoopCandidatesJson");
                        bool hasRating = colSet.Contains("Rating");
                        bool hasIsLoved = colSet.Contains("IsLoved");
                        bool hasCover = colSet.Contains("CoverPath");
                        bool hasArtist = colSet.Contains("Artist");
                        bool hasAlbum = colSet.Contains("Album");
                        bool hasAlbumArtist = colSet.Contains("AlbumArtist");

                        string syncSqlFlat = $@"
                            SELECT 
                                FileName, TotalSamples, 
                                {(hasDisplayName ? "DisplayName" : "FileName AS DisplayName")}, 
                                {(hasArtist ? "Artist" : "NULL AS Artist")}, 
                                {(hasAlbum ? "Album" : "NULL AS Album")}, 
                                {(hasAlbumArtist ? "AlbumArtist" : (hasArtist ? "Artist AS AlbumArtist" : "NULL AS AlbumArtist"))}, 
                                LoopStart, LoopEnd, 
                                {(hasCandidates ? "LoopCandidatesJson" : "NULL AS LoopCandidatesJson")}, 
                                {(hasRating ? "Rating" : "0 AS Rating")}, 
                                {(hasCover ? "CoverPath" : "NULL AS CoverPath")} 
                            FROM ExternalDB.LoopPoints";
                        externalTracks = db.Query<MusicTrack>(syncSqlFlat).ToList();
                    }
                    else
                    {
                        externalTracks = new List<MusicTrack>();
                    }

                    // --- 2. 曲目同步（模糊匹配） ---
                    foreach (var ext in externalTracks)
                    {
                        var localTrack = db.QueryFirstOrDefault<MusicTrack>(@"
                            SELECT Id FROM Tracks 
                            WHERE LOWER(TRIM(FileName)) = LOWER(TRIM(@FileName)) 
                              AND ABS(TotalSamples - @TotalSamples) < 10000", 
                            new { ext.FileName, ext.TotalSamples });
                        
                        if (localTrack != null)
                        {
                            using (var trans = db.BeginTransaction())
                            {
                                int? albumId = UpsertArtistAlbum(db, ext.Artist, ext.AlbumArtist, ext.Album, ext.CoverPath, trans);
                                db.Execute(@"UPDATE Tracks SET DisplayName=@DisplayName, AlbumId=@AlbumId, CoverPath=COALESCE(@CoverPath, CoverPath) WHERE Id=@Id", new { ext.DisplayName, AlbumId = albumId, ext.CoverPath, Id = localTrack.Id }, transaction: trans);
                                db.Execute(@"INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson) VALUES (@Id, @LoopStart, @LoopEnd, @Json) ON CONFLICT(TrackId) DO UPDATE SET LoopStart=excluded.LoopStart, LoopEnd=excluded.LoopEnd, LoopCandidatesJson=excluded.LoopCandidatesJson", new { Id = localTrack.Id, ext.LoopStart, ext.LoopEnd, Json = ext.LoopCandidatesJson }, transaction: trans);
                                db.Execute(@"INSERT INTO UserRatings (TrackId, Rating, LastModified) VALUES (@Id, @Rating, @Now) ON CONFLICT(TrackId) DO UPDATE SET Rating=excluded.Rating", new { Id = localTrack.Id, ext.Rating, Now = DateTime.Now }, transaction: trans);
                                trans.Commit();
                                tracksSynced++;
                            }
                        }
                    }
                    
                    // --- 3. 歌单同步（模糊关联本地 ID） ---
                    if (tables.Contains("Playlists"))
                    {
                        var playlistCols = db.Query("PRAGMA ExternalDB.table_info(Playlists)").Cast<IDictionary<string, object>>()
                                             .Select(c => c["name"].ToString()).ToList();
                        bool hasSortOrder = playlistCols.Contains("SortOrder");

                        // 3.1 同步歌单基本信息
                        string plSyncSql = $@"
                            INSERT INTO Playlists (Name, SortOrder) 
                            SELECT Name, {(hasSortOrder ? "SortOrder" : "0")} 
                            FROM ExternalDB.Playlists 
                            WHERE Name NOT IN (SELECT Name FROM Playlists)";
                        db.Execute(plSyncSql);
                        
                        var playlistMap = db.Query<PlaylistDto>("SELECT Name, Id FROM Playlists")
                                            .GroupBy(x => x.Name)
                                            .ToDictionary(g => g.Key, g => g.First().Id);
                        var externalPlaylists = db.Query<PlaylistDto>("SELECT Id, Name FROM ExternalDB.Playlists");

                        foreach (var extPl in externalPlaylists)
                        {
                            if (playlistMap.TryGetValue(extPl.Name, out int localPlId))
                            {
                                // 3.2 同步歌单项 (PlaylistItems)
                                string itemSyncSql;
                                if (is3NF)
                                {
                                    itemSyncSql = @"
                                        INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId, SortOrder)
                                        SELECT @LocalPlId, t.Id, epi.SortOrder
                                        FROM ExternalDB.PlaylistItems epi
                                        JOIN ExternalDB.Tracks ext_t ON epi.SongId = ext_t.Id
                                        JOIN Tracks t ON LOWER(TRIM(t.FileName)) = LOWER(TRIM(ext_t.FileName)) 
                                                     AND ABS(t.TotalSamples - ext_t.TotalSamples) < 10000
                                        WHERE epi.PlaylistId = @ExtPlId";
                                }
                                else
                                {
                                    // 检查是否有关联表，Flat 架构可能完全没有 PlaylistItems
                                    if (!tables.Contains("PlaylistItems")) continue;

                                    itemSyncSql = @"
                                        INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId, SortOrder)
                                        SELECT @LocalPlId, t.Id, epi.SortOrder
                                        FROM ExternalDB.PlaylistItems epi
                                        JOIN ExternalDB.LoopPoints elp ON epi.SongId = elp.Id
                                        JOIN Tracks t ON LOWER(TRIM(t.FileName)) = LOWER(TRIM(elp.FileName)) 
                                                     AND ABS(t.TotalSamples - elp.TotalSamples) < 10000
                                        WHERE epi.PlaylistId = @ExtPlId";
                                }
                                
                                db.Execute(itemSyncSql, new { LocalPlId = localPlId, ExtPlId = extPl.Id });
                            }
                        }
                        playlistsSynced = externalPlaylists.Count();
                    }
                }
                finally { db.Execute("DETACH DATABASE ExternalDB"); }
            }
            return (tracksSynced, playlistsSynced);
        }

        private void ApplyMigrations(IDbConnection db)
        {
            // 1. 歌单查重与合并 (针对已存在数据的用户)
            // 将重复歌单里的歌曲搬运到 ID 最小的那个同名歌单里
            db.Execute(@"
                INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId, SortOrder)
                SELECT (SELECT MIN(Id) FROM Playlists p2 WHERE p2.Name = p.Name), pi.SongId, pi.SortOrder
                FROM PlaylistItems pi
                JOIN Playlists p ON pi.PlaylistId = p.Id
                WHERE p.Id NOT IN (SELECT MIN(Id) FROM Playlists GROUP BY Name)");

            // 删除已经被搬空的重复歌单
            db.Execute(@"DELETE FROM Playlists WHERE Id NOT IN (SELECT MIN(Id) FROM Playlists GROUP BY Name)");

            // 2. 强制创建唯一索引（如果不存在），确保未来不会再出现重名
            db.Execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_playlists_name ON Playlists(Name)");
        }
        public int CleanupMissingFiles()
        {
            int deletedCount = 0;
            using (var db = GetConnection())
            {
                var tracks = db.Query<(int Id, string FilePath)>("SELECT Id, FilePath FROM Tracks").ToList();
                using (var trans = db.BeginTransaction())
                {
                    foreach (var track in tracks)
                    {
                        if (!string.IsNullOrEmpty(track.FilePath) && !File.Exists(track.FilePath))
                        {
                            db.Execute("DELETE FROM Tracks WHERE Id = @Id", new { Id = track.Id }, transaction: trans);
                            deletedCount++;
                        }
                    }
                    trans.Commit();
                }
            }
            return deletedCount;
        }
        public void RepairMissingCategoryCovers()
        {
            using (var db = GetConnection())
            {
                // 1. 【向上扩散】曲目封面 -> 专辑封面
                db.Execute(@"
                    UPDATE Albums 
                    SET CoverPath = (
                        SELECT t.CoverPath 
                        FROM Tracks t 
                        WHERE t.AlbumId = Albums.Id 
                          AND t.CoverPath IS NOT NULL 
                          AND t.CoverPath != '' 
                        LIMIT 1
                    )
                    WHERE (CoverPath IS NULL OR CoverPath = '')
                      AND EXISTS (
                        SELECT 1 FROM Tracks t 
                        WHERE t.AlbumId = Albums.Id 
                          AND t.CoverPath IS NOT NULL 
                          AND t.CoverPath != ''
                      )");

                // 2. 【向上扩散】专辑封面 -> 艺术家封面
                db.Execute(@"
                    UPDATE Artists 
                    SET CoverPath = (
                        SELECT al.CoverPath 
                        FROM Albums al 
                        WHERE al.ArtistId = Artists.Id 
                          AND al.CoverPath IS NOT NULL 
                          AND al.CoverPath != '' 
                        LIMIT 1
                    )
                    WHERE (CoverPath IS NULL OR CoverPath = '')
                      AND EXISTS (
                        SELECT 1 FROM Albums al 
                        WHERE al.ArtistId = Artists.Id 
                          AND al.CoverPath IS NOT NULL 
                          AND al.CoverPath != ''
                      )");

                // 3. 【向下补完】专辑封面 -> 曲目封面 (核心：确保同专辑曲目同步)
                db.Execute(@"
                    UPDATE Tracks 
                    SET CoverPath = (
                        SELECT al.CoverPath 
                        FROM Albums al 
                        WHERE al.Id = Tracks.AlbumId
                    )
                    WHERE (CoverPath IS NULL OR CoverPath = '')
                      AND EXISTS (
                        SELECT 1 FROM Albums al 
                        WHERE al.Id = Tracks.AlbumId 
                          AND al.CoverPath IS NOT NULL 
                          AND al.CoverPath != ''
                      )");
            }
        }
    }
}
