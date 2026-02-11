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
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LoopData.db");
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
                
                // 1. 核心资源库 (原 LoopPoints)
                string sqlSongs = @"
                    CREATE TABLE IF NOT EXISTS LoopPoints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FileName TEXT NOT NULL,
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
                // 1. 尝试寻找精准匹配（文件名 + 采样数）
                var track = db.QueryFirstOrDefault<MusicTrack>(
                    "SELECT * FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @Total", 
                    new { FileName = fileName, Total = totalSamples });

                // 2. 如果没找到，则只寻找此文件关联的别名
                if (track == null)
                {
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
                            LoopStart = @LoopStart, 
                            LoopEnd = @LoopEnd, 
                            LastModified = @LastModified
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
                        (FileName, DisplayName, LoopStart, LoopEnd, TotalSamples, LastModified)
                        VALUES 
                        (@FileName, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @LastModified);
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
                        (FileName, DisplayName, LoopStart, LoopEnd, TotalSamples, LastModified)
                        VALUES 
                        (@FileName, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @LastModified);";
                    db.Execute(sql, tracks, transaction: trans);
                    trans.Commit();
                }
            }
        }
        // --- 歌单管理新方法 ---

        public void AddPlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                db.Execute("INSERT INTO Playlists (Name, FolderPath, IsFolderLinked) VALUES (@Name, @Path, @Linked)", 
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

        public void AddSongToPlaylist(int playlistId, int songId)
        {
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
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
                string sql = @"
                    SELECT s.*, pi.SortOrder 
                    FROM LoopPoints s
                    JOIN PlaylistItems pi ON s.Id = pi.SongId
                    WHERE pi.PlaylistId = @Pid
                    ORDER BY pi.SortOrder ASC";
                return db.Query<MusicTrack>(sql, new { Pid = playlistId });
            }
        }
    }
}
