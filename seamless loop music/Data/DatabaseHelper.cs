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
                // 使用 (FileName, TotalSamples) 作为唯一标识，彻底抛弃死板的路径
                string sql = @"
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
                db.Execute(sql);
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
    }
}
