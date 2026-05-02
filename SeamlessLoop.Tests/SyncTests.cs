using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Dapper;
using System.Data.SQLite;
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using seamless_loop_music.Data.Repositories;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class SyncTests
    {
        private string _localDbPath;
        private string _externalDbPath;
        private DatabaseHelper _dbHelper;
        private TrackRepository _trackRepo;

        [SetUp]
        public void SetUp()
        {
            _localDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LocalTest.db");
            _externalDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExternalTest.db");

            if (File.Exists(_localDbPath)) File.Delete(_localDbPath);
            if (File.Exists(_externalDbPath)) File.Delete(_externalDbPath);

            // 初始化本地 3NF 数据库
            _dbHelper = new DatabaseHelper(_localDbPath);
            _dbHelper.InitializeDatabase();
            _trackRepo = new TrackRepository(_localDbPath);
        }

        [TearDown]
        public void TearDown()
        {
            // 确保连接释放，否则删不掉文件
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (File.Exists(_localDbPath)) File.Delete(_localDbPath);
            if (File.Exists(_externalDbPath)) File.Delete(_externalDbPath);
        }

        private SQLiteConnection GetExternalConn()
        {
            var conn = new SQLiteConnection($"Data Source={_externalDbPath};Version=3;");
            conn.Open();
            return conn;
        }

        [Test]
        public void Sync_From_FlatSchema_ShouldWork()
        {
            // 1. 准备外部 Flat 架构数据库（单表大杂烩）
            using (var conn = GetExternalConn())
            {
                conn.Execute(@"
                    CREATE TABLE LoopPoints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FileName TEXT,
                        TotalSamples INTEGER,
                        DisplayName TEXT,
                        Artist TEXT,
                        Album TEXT,
                        LoopStart INTEGER,
                        LoopEnd INTEGER,
                        Rating INTEGER
                    )");
                conn.Execute(@"
                    INSERT INTO LoopPoints (FileName, TotalSamples, DisplayName, Artist, LoopStart, LoopEnd, Rating)
                    VALUES ('test.mp3', 441000, 'Test Track', 'Lev Zenith', 100, 200, 5)");
            }

            // 2. 本地先插一个占位曲目（模拟本地已扫描到文件，但没数据）
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, FilePath) VALUES ('test.mp3', 441000, 'C:\\test.mp3')");
            }

            // 3. 执行同步
            var (tracks, playlists) = _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证
            Assert.That(tracks, Is.EqualTo(1), "应同步一个曲目");
            
            var syncedTrack = _trackRepo.GetAllAsync().Result.First();
            Assert.That(syncedTrack.DisplayName, Is.EqualTo("Test Track"));
            Assert.That(syncedTrack.Artist, Is.EqualTo("Lev Zenith"));
            Assert.That(syncedTrack.LoopStart, Is.EqualTo(100));
            Assert.That(syncedTrack.Rating, Is.EqualTo(5));
        }

        [Test]
        public void Sync_From_Old3NF_ShouldDropFolderColumns()
        {
            // 1. 准备外部 Old 3NF 架构（带 FolderPath 字段）
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER)");
                conn.Execute("CREATE TABLE Playlists (Id INTEGER PRIMARY KEY, Name TEXT, FolderPath TEXT, IsFolderLinked INTEGER, SortOrder INTEGER)");
                conn.Execute("CREATE TABLE PlaylistItems (PlaylistId INTEGER, SongId INTEGER, SortOrder INTEGER)");
                
                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (1, 'song.wav', 1000)");
                conn.Execute("INSERT INTO Playlists (Id, Name, FolderPath, IsFolderLinked, SortOrder) VALUES (10, 'Old Playlist', 'C:\\Music', 1, 5)");
                conn.Execute("INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (10, 1, 0)");
            }

            // 2. 本地占位
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples) VALUES ('song.wav', 1000)");
            }

            // 3. 同步
            var (tracks, playlists) = _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证
            Assert.That(playlists, Is.EqualTo(1), "应同步一个歌单");
            
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                var pl = conn.QueryFirstOrDefault("SELECT * FROM Playlists WHERE Name = 'Old Playlist'");
                Assert.That(pl, Is.Not.Null);
                Assert.That((int)pl.SortOrder, Is.EqualTo(5));
                
                // 验证曲目是否关联成功
                int itemCount = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Id", new { pl.Id });
                Assert.That(itemCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Sync_WithMissingPlaylistsTable_ShouldNotCrash()
        {
            // 1. 准备一个只有曲目表、没歌单表的损坏库
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Tracks (FileName TEXT, TotalSamples INTEGER)");
            }

            // 2. 执行同步，不应抛出异常
            Assert.DoesNotThrow(() => {
                _dbHelper.SyncWithExternalDatabase(_externalDbPath);
            });
        }

        [Test]
        public void Sync_ShouldMergeDuplicatePlaylists()
        {
            // 1. 本地已有一个 "MyList"
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Playlists (Name) VALUES ('MyList')");
            }

            // 2. 外部也有一个 "MyList"
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Playlists (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("INSERT INTO Playlists (Name) VALUES ('MyList')");
            }

            // 3. 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证本地依然只有一个 "MyList" (由 ApplyMigrations 保证)
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                int count = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Playlists WHERE Name = 'MyList'");
                Assert.That(count, Is.EqualTo(1), "同名歌单应合并，不应重复");
            }
        }
    }
}
