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
    public class DatabaseHelper
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseHelper()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(baseDir, "Data");
            
            // 确保 Data 目录存在
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            _dbPath = Path.Combine(dataDir, "LoopData.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (!File.Exists(_dbPath))
            {
                SQLiteConnection.CreateFile(_dbPath);
            }

            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Open();
                db.Execute("PRAGMA foreign_keys = ON;"); // 强制开启外键支持
                db.Execute("PRAGMA journal_mode = WAL;"); // 开启 WAL 模式以提升并发性能

                // 1. 核心资源库 (原 LoopPoints)
                string sqlSongs = @"
                    CREATE TABLE IF NOT EXISTS LoopPoints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FileName TEXT NOT NULL,
                        FilePath TEXT, -- 存储最后一次被扫描到的物理路径
                        TotalSamples INTEGER NOT NULL,
                        DisplayName TEXT,
                        LoopStart INTEGER NOT NULL,
                        LoopEnd INTEGER NOT NULL,
                        LastModified DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(FileName, TotalSamples)
                    );";
                db.Execute(sqlSongs);

                // 2. 歌单定义表
                string sqlPlaylists = @"
                    CREATE TABLE IF NOT EXISTS Playlists (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        FolderPath TEXT, -- 关联的物理路径 (可选)
                        IsFolderLinked INTEGER DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";
                db.Execute(sqlPlaylists);

                // 3. 歌单项表 (关联歌曲与歌单)
                string sqlPlaylistItems = @"
                    CREATE TABLE IF NOT EXISTS PlaylistItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PlaylistId INTEGER NOT NULL,
                        SongId INTEGER NOT NULL,
                        SortOrder INTEGER DEFAULT 0,
                        FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                        FOREIGN KEY(SongId) REFERENCES LoopPoints(Id) ON DELETE CASCADE
                    );";
                db.Execute(sqlPlaylistItems);

                // 4. 歌单源文件夹表 (支持多文件夹关联)
                string sqlPlaylistFolders = @"
                    CREATE TABLE IF NOT EXISTS PlaylistFolders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PlaylistId INTEGER NOT NULL,
                        FolderPath TEXT NOT NULL,
                        AddedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                        UNIQUE(PlaylistId, FolderPath)
                    );";
                db.Execute(sqlPlaylistFolders);

                // --- 自动升级：检查并添加 FilePath 字段 ---
                // 不使用 dynamic，改用强类型或直接尝试查询
                var hasFilePath = false;
                using (var reader = ((SQLiteConnection)db).ExecuteReader("PRAGMA table_info(LoopPoints)"))
                {
                    while (reader.Read())
                    {
                        if (reader["name"].ToString() == "FilePath")
                        {
                            hasFilePath = true;
                            break;
                        }
                    }
                }

                if (!hasFilePath)
                {
                    db.Execute("ALTER TABLE LoopPoints ADD COLUMN FilePath TEXT;");
                }

                // 检查并添加 LoopPoints.LoopCandidatesJson
                using (var reader = ((SQLiteConnection)db).ExecuteReader("PRAGMA table_info(LoopPoints)"))
                {
                    var hasJson = false;
                    while (reader.Read())
                    {
                        if (reader["name"].ToString().Equals("LoopCandidatesJson", StringComparison.OrdinalIgnoreCase))
                        {
                            hasJson = true; break;
                        }
                    }
                    if (!hasJson) db.Execute("ALTER TABLE LoopPoints ADD COLUMN LoopCandidatesJson TEXT;");
                }

                // 检查并添加 Playlists.SortOrder
                using (var reader = ((SQLiteConnection)db).ExecuteReader("PRAGMA table_info(Playlists)"))
                {
                    var hasSort = false;
                    while (reader.Read())
                    {
                        if (reader["name"].ToString().Equals("SortOrder", StringComparison.OrdinalIgnoreCase))
                        {
                            hasSort = true; break;
                        }
                    }
                    if (!hasSort) db.Execute("ALTER TABLE Playlists ADD COLUMN SortOrder INTEGER DEFAULT 0;");
                }
            }
        }

        /// <summary>
        /// 获取所有保存的循环配置
        /// </summary>
        public IEnumerable<MusicTrack> GetAllTracks()
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                return db.Query<MusicTrack>("SELECT * FROM LoopPoints");
            }
        }

        /// <summary>
        /// 根据指纹（文件名+总采样数）寻找配置
        /// </summary>
        public MusicTrack GetTrack(string fullPath, long totalSamples)
        {
            string fileName = Path.GetFileName(fullPath);

            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                // 确保包含 FilePath 字段
                var track = db.QueryFirstOrDefault<MusicTrack>(
                    "SELECT Id, FileName, FilePath, TotalSamples, DisplayName, LoopStart, LoopEnd, LastModified, LoopCandidatesJson FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @Total", 
                    new { FileName = fileName, Total = totalSamples });

                if (track != null) {
                    // 如果数据库里存了路径，就用数据库的（除非当前路径更准）
                    if (string.IsNullOrEmpty(track.FilePath)) track.FilePath = fullPath;
                } else {
                    // 只找别名逻辑
                    var alias = db.ExecuteScalar<string>(
                        "SELECT DisplayName FROM LoopPoints WHERE FileName = @FileName AND DisplayName IS NOT NULL LIMIT 1",
                        new { FileName = fileName });
                    
                    if (!string.IsNullOrEmpty(alias))
                    {
                        track = new MusicTrack { FilePath = fullPath, FileName = fileName, DisplayName = alias, TotalSamples = totalSamples };
                    }
                }
                return track;
            }
        }

        public void SaveTrack(MusicTrack track)
        {
            if (string.IsNullOrEmpty(track.FileName) && !string.IsNullOrEmpty(track.FilePath))
                track.FileName = Path.GetFileName(track.FilePath);

            track.LastModified = DateTime.Now;
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                if (track.Id > 0)
                {
                    // 已有 ID，执行精确更新
                    string sql = @"
                        UPDATE LoopPoints 
                        SET DisplayName = @DisplayName, 
                            FilePath = @FilePath,
                            LoopStart = @LoopStart, 
                            LoopEnd = @LoopEnd, 
                            LastModified = @LastModified,
                            LoopCandidatesJson = @LoopCandidatesJson
                        WHERE Id = @Id;";
                    db.Execute(sql, track);
                }
                else
                {
                    // 没有 ID，尝试插入或忽略（如果唯一键冲突则啥也不干，后续查询再拿 ID）
                    // 但为了保证逻辑正确，我们这里用 INSERT OR IGNORE + SELECT last_insert_rowid
                    // 或者更简单的：如果插入失败（冲突），则由上层逻辑保证先 Get 再 Save。
                    // 鉴于我们现在的逻辑是 Update-First，这里用 INSERT 即可。
                    string sql = @"
                        INSERT INTO LoopPoints 
                        (FileName, FilePath, DisplayName, LoopStart, LoopEnd, TotalSamples, LastModified, LoopCandidatesJson)
                        VALUES 
                        (@FileName, @FilePath, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @LastModified, @LoopCandidatesJson);
                        SELECT last_insert_rowid();";
                    
                    try {
                        long newId = db.ExecuteScalar<long>(sql, track);
                        track.Id = (int)newId;
                    } catch (SQLiteException) {
                        // 唯一键冲突？说明数据库里其实有，通过指纹（FileName+Samples）反查 ID
                        var existing = db.QueryFirstOrDefault<MusicTrack>(
                            "SELECT Id FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @Total", 
                            new { FileName = track.FileName, Total = track.TotalSamples });
                        if (existing != null) {
                            track.Id = existing.Id;
                            // 既然已存在，就补一个 UPDATE
                            SaveTrack(track); 
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 批量迁移导入专用
        /// </summary>
        public void BulkInsert(IEnumerable<MusicTrack> tracks)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Open();
                using (var trans = db.BeginTransaction())
                {
                    string sql = @"
                        INSERT OR REPLACE INTO LoopPoints 
                        (FileName, FilePath, DisplayName, LoopStart, LoopEnd, TotalSamples, LastModified, LoopCandidatesJson)
                        VALUES 
                        (@FileName, @FilePath, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @LastModified, @LoopCandidatesJson);";
                    db.Execute(sql, tracks, transaction: trans);
                    trans.Commit();
                }
            }
        }
        // --- 歌单管理新方法 ---

        public IEnumerable<PlaylistFolder> GetAllPlaylists()
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                // 使用 SQL 别名将数据库的 FolderPath 映射到模型的 Path
                string sql = @"
                    SELECT 
                        p.Id, 
                        p.Name, 
                        p.FolderPath AS Path, 
                        p.IsFolderLinked, 
                        p.CreatedAt,
                        p.SortOrder,
                        (SELECT COUNT(1) FROM PlaylistItems pi WHERE pi.PlaylistId = p.Id) AS SongCount
                    FROM Playlists p
                    ORDER BY p.SortOrder ASC, p.CreatedAt DESC";
                return db.Query<PlaylistFolder>(sql);
            }
        }

        public int AddPlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
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
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Execute("DELETE FROM Playlists WHERE Id = @Id", new { Id = playlistId });
            }
        }

        public void BulkSaveTracksAndAddToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var track in tracks)
                        {
                            // 1. Save Track (Upsert)
                            var existing = connection.QueryFirstOrDefault<MusicTrack>(
                                "SELECT * FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @TotalSamples", 
                                new { track.FileName, track.TotalSamples }, transaction);

                            long trackId;
                            if (existing == null)
                            {
                                track.Id = (int)connection.QuerySingle<long>(
                                    @"INSERT INTO LoopPoints (FileName, FilePath, TotalSamples, DisplayName, LoopStart, LoopEnd, LastModified, LoopCandidatesJson) 
                                      VALUES (@FileName, @FilePath, @TotalSamples, @DisplayName, @LoopStart, @LoopEnd, @LastModified, @LoopCandidatesJson);
                                      SELECT last_insert_rowid();", track, transaction);
                                trackId = track.Id;
                            }
                            else
                            {
                                trackId = existing.Id;
                                // Update FilePath if changed
                                if (existing.FilePath != track.FilePath)
                                {
                                    connection.Execute("UPDATE LoopPoints SET FilePath = @FilePath WHERE Id = @Id", 
                                        new { track.FilePath, Id = trackId }, transaction);
                                }
                            }

                            // 2. Add to Playlist if not exists
                            var count = connection.ExecuteScalar<int>(
                                "SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @PlaylistId AND SongId = @MusicTrackId", 
                                new { PlaylistId = playlistId, MusicTrackId = trackId }, transaction);

                            if (count == 0)
                            {
                                connection.Execute(
                                    "INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@PlaylistId, @MusicTrackId, 0)", 
                                    new { PlaylistId = playlistId, MusicTrackId = trackId }, transaction);
                            }
                        }
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public void AddSongToPlaylist(int playlistId, int songId)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                // 防止重复添加
                int existing = db.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Pid AND SongId = @Sid", 
                    new { Pid = playlistId, Sid = songId });
                
                if (existing > 0) return;

                // 获取当前最大排序值
                int maxOrder = db.ExecuteScalar<int>("SELECT IFNULL(MAX(SortOrder), 0) FROM PlaylistItems WHERE PlaylistId = @Pid", new { Pid = playlistId });
                db.Execute("INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@Pid, @Sid, @Order)", 
                    new { Pid = playlistId, Sid = songId, Order = maxOrder + 1 });
            }
        }

        public void RemoveSongFromPlaylist(int playlistId, int songId)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Execute("DELETE FROM PlaylistItems WHERE PlaylistId = @Pid AND SongId = @Sid", new { Pid = playlistId, Sid = songId });
            }
        }

        /// <summary>
        /// 获取歌单内的所有曲目
        /// </summary>
        public IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                // 明确选取 s.FilePath
                string sql = @"
                    SELECT s.Id, s.FileName, s.FilePath, s.TotalSamples, s.DisplayName, s.LoopStart, s.LoopEnd, s.LastModified, s.LoopCandidatesJson, pi.SortOrder 
                    FROM LoopPoints s
                    JOIN PlaylistItems pi ON s.Id = pi.SongId
                    WHERE pi.PlaylistId = @Pid
                    ORDER BY pi.SortOrder ASC";
                return db.Query<MusicTrack>(sql, new { Pid = playlistId });
            }
        }
        public void RenamePlaylist(int playlistId, string newName)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Execute("UPDATE Playlists SET Name = @Name WHERE Id = @Id", new { Name = newName, Id = playlistId });
            }
        }

        /// <summary>
        /// 检查歌单内是否已存在某首歌
        /// </summary>
        public bool IsSongInPlaylist(int playlistId, int songId)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                return db.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Pid AND SongId = @Sid", 
                    new { Pid = playlistId, Sid = songId }) > 0;
            }
        }

        public void UpdatePlaylistsSortOrder(List<int> playlistIds)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Open();
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
        }

        public void UpdateTracksSortOrder(int playlistId, List<int> songIds)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Open();
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
        }

        // --- 歌单文件夹管理 ---

        public void AddPlaylistFolder(int playlistId, string folderPath)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                // 使用 INSERT OR IGNORE 避免重复添加
                db.Execute(@"
                    INSERT OR IGNORE INTO PlaylistFolders (PlaylistId, FolderPath) 
                    VALUES (@Pid, @Path)", 
                    new { Pid = playlistId, Path = folderPath });
            }
        }

        public void RemovePlaylistFolder(int playlistId, string folderPath)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Execute("DELETE FROM PlaylistFolders WHERE PlaylistId = @Pid AND FolderPath = @Path", 
                    new { Pid = playlistId, Path = folderPath });
            }
        }

        public List<string> GetPlaylistFolders(int playlistId)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                return db.Query<string>("SELECT FolderPath FROM PlaylistFolders WHERE PlaylistId = @Pid ORDER BY AddedAt ASC", 
                    new { Pid = playlistId }).ToList();
            }
        }
    }
}
