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

        [Test]
        public void Sync_From_Intermediate3NF_ShouldMigrateArtistToTrack()
        {
            // 场景：外部库是“旧版 3NF”（ArtistId 在 Albums 表上）
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Artists (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Albums (Id INTEGER PRIMARY KEY, Name TEXT, ArtistId INTEGER)");
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER, AlbumId INTEGER)");
                
                conn.Execute("INSERT INTO Artists (Id, Name) VALUES (1, 'Legacy Artist')");
                conn.Execute("INSERT INTO Albums (Id, Name, ArtistId) VALUES (10, 'Legacy Album', 1)");
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, AlbumId) VALUES ('song.mp3', 1000, 10)");
            }

            // 本地占位
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples) VALUES ('song.mp3', 1000)");
            }

            // 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 验证：本地 Tracks 表是否正确拿到了 ArtistId
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                var track = conn.QueryFirstOrDefault("SELECT t.*, ar.Name as ArtistName FROM Tracks t JOIN Artists ar ON t.ArtistId = ar.Id WHERE t.FileName = 'song.mp3'");
                Assert.That(track, Is.Not.Null);
                Assert.That((string)track.ArtistName, Is.EqualTo("Legacy Artist"), "同步应从旧版 Albums.ArtistId 映射到本地的 Tracks.ArtistId");
            }
        }

        [Test]
        public void Sync_From_New3NF_ShouldSyncDirectly()
        {
            // 场景：外部库已经是“新版 3NF”（ArtistId 在 Tracks 表上）
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Artists (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Albums (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER, AlbumId INTEGER, ArtistId INTEGER)");
                
                conn.Execute("INSERT INTO Artists (Id, Name) VALUES (1, 'New Artist')");
                conn.Execute("INSERT INTO Albums (Id, Name) VALUES (10, 'New Album')");
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, AlbumId, ArtistId) VALUES ('new.mp3', 2000, 10, 1)");
            }

            // 本地占位
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples) VALUES ('new.mp3', 2000)");
            }

            // 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 验证
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                var track = conn.QueryFirstOrDefault("SELECT t.*, ar.Name as ArtistName FROM Tracks t JOIN Artists ar ON t.ArtistId = ar.Id WHERE t.FileName = 'new.mp3'");
                Assert.That(track, Is.Not.Null);
                Assert.That((string)track.ArtistName, Is.EqualTo("New Artist"), "同步应直接从外部 Tracks.ArtistId 映射到本地");
            }
        }

        [Test]
        public void Sync_ShouldNotOverwriteLocalCoverPath_WithExternalCover()
        {
            // 1. 准备外部 3NF 数据库，包含一个有封面的曲目
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Artists (Id INTEGER PRIMARY KEY, Name TEXT, CoverPath TEXT)");
                conn.Execute("CREATE TABLE Albums (Id INTEGER PRIMARY KEY, Name TEXT, CoverPath TEXT)");
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER, AlbumId INTEGER, ArtistId INTEGER, CoverPath TEXT)");
                
                conn.Execute("INSERT INTO Artists (Id, Name, CoverPath) VALUES (1, 'Test Artist', 'D:\\External\\ArtistCover.jpg')");
                conn.Execute("INSERT INTO Albums (Id, Name, CoverPath) VALUES (10, 'Test Album', 'D:\\External\\AlbumCover.jpg')");
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, AlbumId, ArtistId, CoverPath) VALUES ('test.mp3', 441000, 10, 1, 'D:\\External\\TrackCover.jpg')");
            }

            // 2. 本地占位，并设置一个已有的本地有效封面路径
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, CoverPath) VALUES ('test.mp3', 441000, 'C:\\Local\\TrackCover.jpg')");
            }

            // 3. 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证本地的 CoverPath 没有被外部的覆盖破坏
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                var localTrack = conn.QueryFirstOrDefault("SELECT * FROM Tracks WHERE FileName = 'test.mp3'");
                Assert.That(localTrack, Is.Not.Null);
                Assert.That((string)localTrack.CoverPath, Is.EqualTo("C:\\Local\\TrackCover.jpg"), "本地曲目的 CoverPath 不应被外部封面路径覆盖");

                // 另外验证同步新建的 Artists 和 Albums 的 CoverPath 是否为 null，免于被外部无效路径污染
                var localArtist = conn.QueryFirstOrDefault("SELECT * FROM Artists WHERE Name = 'Test Artist'");
                Assert.That(localArtist, Is.Not.Null);
                Assert.That((string)localArtist.CoverPath, Is.Null, "同步自动创建的 Artist 封面路径应保持为 null，不被外部无效封面污染");

                var localAlbum = conn.QueryFirstOrDefault("SELECT * FROM Albums WHERE Name = 'Test Album'");
                Assert.That(localAlbum, Is.Not.Null);
                Assert.That((string)localAlbum.CoverPath, Is.Null, "同步自动创建的 Album 封面路径应保持为 null，不被外部无效封面污染");
            }
        }

        [Test]
        public void Sync_ShouldNotOverwriteLocalRating_WithExternalZeroOrNonZero()
        {
            // 1. 准备外部 3NF 数据库，包含两个曲目：一个有评分(4)，一个无评分(0)
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Artists (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Albums (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER, AlbumId INTEGER, ArtistId INTEGER)");
                conn.Execute("CREATE TABLE UserRatings (TrackId INTEGER PRIMARY KEY, Rating INTEGER)");
                
                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (1, 'songA.mp3', 1000)");
                conn.Execute("INSERT INTO UserRatings (TrackId, Rating) VALUES (1, 4)");

                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (2, 'songB.mp3', 2000)");
                conn.Execute("INSERT INTO UserRatings (TrackId, Rating) VALUES (2, 0)");
            }

            // 2. 本地占位：
            // songA: 本地已有评分(5) -> 同步时不应被外部的 4 星覆盖
            // songC: 本地评分(0)，外部评分(3) -> 应成功补充为 3 星
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (10, 'songA.mp3', 1000)");
                conn.Execute("INSERT INTO UserRatings (TrackId, Rating) VALUES (10, 5)");

                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (20, 'songC.mp3', 3000)");
                conn.Execute("INSERT INTO UserRatings (TrackId, Rating) VALUES (20, 0)");
            }

            using (var conn = GetExternalConn())
            {
                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (3, 'songC.mp3', 3000)");
                conn.Execute("INSERT INTO UserRatings (TrackId, Rating) VALUES (3, 3)");
            }

            // 3. 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                // songA 本地5星保持不变
                int ratingA = conn.ExecuteScalar<int>("SELECT Rating FROM UserRatings WHERE TrackId = 10");
                Assert.That(ratingA, Is.EqualTo(5), "本地已有非0评分不应被外部任何评分覆盖");

                // songC 本地0星，成功被外部的 3 星补充
                int ratingC = conn.ExecuteScalar<int>("SELECT Rating FROM UserRatings WHERE TrackId = 20");
                Assert.That(ratingC, Is.EqualTo(3), "本地评分为0时应被外部有效评分补充");
            }
        }

        [Test]
        public void Sync_ShouldNotOverwriteLocalLoopPoints_WithExternalZero()
        {
            // 1. 准备外部 3NF 数据库，匹配的曲目循环参数均为 0 (未分析)
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER)");
                conn.Execute("CREATE TABLE LoopPoints (TrackId INTEGER PRIMARY KEY, LoopStart INTEGER, LoopEnd INTEGER, LoopCandidatesJson TEXT)");
                
                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (1, 'loop.mp3', 44100)");
                conn.Execute("INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd) VALUES (1, 0, 0)");
            }

            // 2. 本地占位：已具备有效的循环成果 (100 -> 200)
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT INTO Tracks (Id, FileName, TotalSamples) VALUES (10, 'loop.mp3', 44100)");
                conn.Execute("INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd) VALUES (10, 100, 200)");
            }

            // 3. 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证：本地已有的循环成果没有被外部的 0 覆盖抹杀
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                var lp = conn.QueryFirstOrDefault("SELECT * FROM LoopPoints WHERE TrackId = 10");
                Assert.That(lp, Is.Not.Null);
                Assert.That((int)lp.LoopStart, Is.EqualTo(100), "外部未分析的数据不应抹杀本地已计算好的有效循环参数");
                Assert.That((int)lp.LoopEnd, Is.EqualTo(200));
            }
        }

        [Test]
        public void Sync_ShouldComplementMissingMetadata_AndKeepExisting()
        {
            // 1. 准备外部数据库，包含精美完备的元数据
            using (var conn = GetExternalConn())
            {
                conn.Execute("CREATE TABLE Artists (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Albums (Id INTEGER PRIMARY KEY, Name TEXT)");
                conn.Execute("CREATE TABLE Tracks (Id INTEGER PRIMARY KEY, FileName TEXT, TotalSamples INTEGER, AlbumId INTEGER, ArtistId INTEGER, DisplayName TEXT)");
                
                conn.Execute("INSERT INTO Artists (Id, Name) VALUES (1, 'Test Artist')");
                conn.Execute("INSERT INTO Albums (Id, Name) VALUES (10, 'Test Album')");
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, AlbumId, ArtistId, DisplayName) VALUES ('song.mp3', 1000, 10, 1, 'Beautiful DisplayName')");
            }

            // 2. 本地占位：元数据缺失（处于 Unknown 或默认文件名状态）
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                // 本地歌手、专辑均未关联，且显示名为默认文件名
                conn.Execute("INSERT INTO Tracks (FileName, TotalSamples, DisplayName) VALUES ('song.mp3', 1000, 'song.mp3')");
            }

            // 3. 执行同步
            _dbHelper.SyncWithExternalDatabase(_externalDbPath);

            // 4. 验证本地的元数据缺失部分已被成功补充
            using (var conn = new SQLiteConnection($"Data Source={_localDbPath};Version=3;"))
            {
                conn.Open();
                var track = conn.QueryFirstOrDefault(@"
                    SELECT t.*, al.Name AS AlbumName, ar.Name AS ArtistName 
                    FROM Tracks t 
                    LEFT JOIN Albums al ON t.AlbumId = al.Id 
                    LEFT JOIN Artists ar ON t.ArtistId = ar.Id 
                    WHERE t.FileName = 'song.mp3'");
                
                Assert.That(track, Is.Not.Null);
                Assert.That((string)track.DisplayName, Is.EqualTo("Beautiful DisplayName"), "显示名应当被外部的精品元数据回填补充");
                Assert.That((string)track.ArtistName, Is.EqualTo("Test Artist"), "歌手应当被外部歌手回填补充");
                Assert.That((string)track.AlbumName, Is.EqualTo("Test Album"), "专辑应当被外部专辑回填补充");
            }
        }
    }
}
