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
                return db.QueryFirstOrDefault<MusicTrack>(
                    "SELECT * FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @Total", 
                    new { FileName = fileName, Total = totalSamples });
            }
        }

        /// <summary>
        /// 保存或更新配置
        /// </summary>
        public void SaveTrack(MusicTrack track)
        {
            if (string.IsNullOrEmpty(track.FileName) && !string.IsNullOrEmpty(track.FilePath))
                track.FileName = Path.GetFileName(track.FilePath);

            track.LastModified = DateTime.Now;
            using (IDbConnection db = new SQLiteConnection(_connectionString))
            {
                string sql = @"
                    INSERT OR REPLACE INTO LoopPoints 
                    (FileName, DisplayName, LoopStart, LoopEnd, TotalSamples, LastModified)
                    VALUES 
                    (@FileName, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @LastModified);";
                db.Execute(sql, track);
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
