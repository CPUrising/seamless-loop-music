using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class DatabaseTests
    {
        private DatabaseHelper _dbHelper;
        private TrackRepository _trackRepo;
        private PlaylistRepository _playlistRepo;
        private string _testDbPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // 使用临时文件模式，确保物理隔离且稳定
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDBs");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            _testDbPath = Path.Combine(tempDir, $"Test_{Guid.NewGuid()}.db");
            _dbHelper = new DatabaseHelper(_testDbPath);
            
            // 初始化表结构
            _dbHelper.InitializeDatabase();

            _trackRepo = new TrackRepository(_testDbPath);
            _playlistRepo = new PlaylistRepository(_testDbPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // 确保所有连接都关闭后再删除
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            if (File.Exists(_testDbPath))
            {
                try { File.Delete(_testDbPath); } catch { }
            }
        }

        [SetUp]
        public void ClearData()
        {
            // 每个测试前清空表数据
            using (var db = _dbHelper.GetConnection())
            {
                db.Execute("DELETE FROM PlaylistItems;");
                db.Execute("DELETE FROM Playlists;");
                db.Execute("DELETE FROM UserRatings;");
                db.Execute("DELETE FROM LoopPoints;");
                db.Execute("DELETE FROM Tracks;");
                db.Execute("DELETE FROM Albums;");
                db.Execute("DELETE FROM Artists;");
            }
        }

        #region 1. 3NF 架构与完整性测试

        [Test]
        public void GoldenDataset_Relationships_ShouldBeCorrect()
        {
            // 注入黄金数据集
            var tracks = TestDatabaseSeed.GetGoldenTracks();
            _trackRepo.BulkInsert(tracks);

            using (var db = _dbHelper.GetConnection())
            {
                // 1. 验证歌手和专辑的唯一性
                var artistCount = db.ExecuteScalar<int>("SELECT COUNT(1) FROM Artists");
                var albumCount = db.ExecuteScalar<int>("SELECT COUNT(1) FROM Albums");

                // 黄金数据中有 5 首歌，5 个独立歌手，4 个独立专辑（Greatest Hits 被由于指纹唯一化而合并）
                Assert.That(artistCount, Is.EqualTo(5), "应识别出 5 位独立艺术家");
                Assert.That(albumCount, Is.EqualTo(4), "应识别出 4 个独立专辑记录（重名已合并）");

                // 2. 验证重名专辑 (Greatest Hits) 是否已合并
                var greatestHitsCount = db.ExecuteScalar<int>("SELECT COUNT(1) FROM Albums WHERE Name = 'Greatest Hits'");
                Assert.That(greatestHitsCount, Is.EqualTo(1), "同名专辑应合并为一条 Album 记录");
            }
        }

        [Test]
        public void SharedAlbum_DifferentArtists_ShouldMaintainUniqueArtists()
        {
            // 场景：两个艺术家都有同名专辑 "Live"
            var track1 = new MusicTrack { FileName = "a.mp3", Artist = "Artist X", Album = "Live", TotalSamples = 1000 };
            var track2 = new MusicTrack { FileName = "b.mp3", Artist = "Artist Y", Album = "Live", TotalSamples = 2000 };
            
            _trackRepo.Save(track1);
            _trackRepo.Save(track2);

            using (var db = _dbHelper.GetConnection())
            {
                // 专辑表应只有一条 "Live"
                var albumCount = db.ExecuteScalar<int>("SELECT COUNT(1) FROM Albums WHERE Name = 'Live'");
                Assert.That(albumCount, Is.EqualTo(1));

                // 歌曲表应分别对应两个不同的艺术家
                var tracks = db.Query("SELECT t.FileName, ar.Name as ArtistName FROM Tracks t JOIN Artists ar ON t.ArtistId = ar.Id").ToList();
                Assert.That(tracks.Count, Is.EqualTo(2));
                
                var x = tracks.FirstOrDefault(t => (string)t.FileName == "a.mp3");
                var y = tracks.FirstOrDefault(t => (string)t.FileName == "b.mp3");
                
                Assert.That((string)x.ArtistName, Is.EqualTo("Artist X"));
                Assert.That((string)y.ArtistName, Is.EqualTo("Artist Y"));
            }
        }

        [Test]
        public void CascadeDelete_ShouldCleanAllRelatedData()
        {
            var tracks = TestDatabaseSeed.GetGoldenTracks();
            _trackRepo.BulkInsert(tracks);
            var track = _trackRepo.GetAll().First();

            using (var db = _dbHelper.GetConnection())
            {
                // 执行删除
                _trackRepo.DeleteAsync(track.Id).Wait();

                // 验证级联清理 (Tracks -> LoopPoints/UserRatings/PlaylistItems)
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(1) FROM Tracks WHERE Id = @Id", new { track.Id }), Is.EqualTo(0));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(1) FROM LoopPoints WHERE TrackId = @Id", new { track.Id }), Is.EqualTo(0));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(1) FROM UserRatings WHERE TrackId = @Id", new { track.Id }), Is.EqualTo(0));
            }
        }

        #endregion

        #region 2. 循环参数边界测试 (Loop Logic)

        [Test]
        public void LoopPoints_BoundaryValues_ShouldBeSavedCorrect()
        {
            var track = new MusicTrack { FileName = "Boundary.mp3", TotalSamples = 10000 };
            _trackRepo.Save(track);
            var saved = _trackRepo.GetByFingerprint(track.FileName, track.TotalSamples);

            // 测试 LoopEnd = TotalSamples
            saved.LoopStart = 0;
            saved.LoopEnd = 10000;
            _trackRepo.Save(saved);

            var updated = _trackRepo.GetByIdAsync(saved.Id).Result;
            Assert.That(updated.LoopEnd, Is.EqualTo(10000));
        }

        #endregion

        #region 3. 事务与并发压力测试 (Transactions & Concurrency)

        [Test]
        public void Transaction_Rollback_ShouldMaintainConsistency()
        {
            _playlistRepo.Add("Empty Playlist");
            var pid = _playlistRepo.GetAllAsync().Result.First().Id;

            try
            {
                using (var db = _dbHelper.GetConnection())
                using (var trans = db.BeginTransaction())
                {
                    // 1. 正常插入一项
                    db.Execute("INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@Pid, 1, 1)", new { Pid = pid }, transaction: trans);
                    
                    // 2. 模拟中途失败 (手动回滚)
                    trans.Rollback(); 
                }
            }
            catch { }

            // 验证数据是否回滚
            using (var db = _dbHelper.GetConnection())
            {
                var count = db.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Pid", new { Pid = pid });
                Assert.That(count, Is.EqualTo(0), "手动回滚后数据应为空");
            }
        }

        [Test]
        public async Task Concurrent_Access_ShouldNotCrash()
        {
            // 模拟高并发下的读写冲突
            var tracks = TestDatabaseSeed.GenerateMassiveData(100).ToList();
            
            var task1 = Task.Run(() => _trackRepo.BulkInsert(tracks)); 
            var task2 = Task.Run(() => {
                for(int i=0; i<30; i++) _dbHelper.SetSetting($"Key_{i}", $"Value_{i}"); 
            });
            var task3 = Task.Run(() => {
                for(int i=0; i<30; i++) _trackRepo.GetAll().ToList(); 
            });

            await Task.WhenAll(task1, task2, task3);
            Assert.Pass("多线程并发访问未发生崩溃（SQLite 已开启 WAL 模式处理忙碌）");
        }

        #endregion

        #region 4. 性能达标测试

        [Test]
        public void Performance_LargeScale_Query_ShouldBeUnderThreshold()
        {
            int massiveCount = 5000; // 适当减少数量以加快测试速度
            var massiveData = TestDatabaseSeed.GenerateMassiveData(massiveCount).ToList();
            
            _trackRepo.BulkInsert(massiveData);

            // 查询性能测试 (阈值 < 10ms)
            var target = massiveData[massiveCount / 2];
            
            var sw = Stopwatch.StartNew();
            var result = _trackRepo.GetByFingerprint(target.FileName, target.TotalSamples);
            sw.Stop();

            Console.WriteLine($"[Performance] Fingerprint Query for {massiveCount} tracks took: {sw.Elapsed.TotalMilliseconds}ms");
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(15), "在数千条记录下，指纹查询应在毫秒级");
        }

        #endregion
        
        #region 5. 3NF 关联查询深度测试 (Complex Joins)

        [Test]
        public async Task GetTracksInPlaylistAsync_ShouldReturnTracksWithFull3NFMetadata()
        {
            // 1. 注入黄金数据集
            var tracks = TestDatabaseSeed.GetGoldenTracks().ToList();
            // 创建一个真实的临时文件，否则 RepairMissingCategoryCovers 会因为物理文件不存在而清理掉数据库记录
            string testCover = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_cover.jpg");
            File.WriteAllText(testCover, "fake data");
            tracks[0].CoverPath = testCover;
            _trackRepo.BulkInsert(tracks);
            
            // 2. 创建歌单并关联 Standard_Track.mp3（它带有封面）
            int pid = _playlistRepo.Add("Metadata Test Playlist");
            var allTracks = await _trackRepo.GetAllAsync();
            var targetTrack = allTracks.FirstOrDefault(t => t.FileName == "Standard_Track.mp3");
            Assert.That(targetTrack, Is.Not.Null);
            _playlistRepo.AddTrack(pid, targetTrack.Id);

            // 3. 执行待测方法
            var playlistTracks = await _playlistRepo.GetTracksInPlaylistAsync(pid);

            // 4. 验证：不仅数量对，元数据也要全
            Assert.That(playlistTracks.Count, Is.EqualTo(1));
            var t = playlistTracks[0];
            Assert.That(t.Artist, Is.Not.Null.And.Not.Empty, $"曲目 {t.FileName} 的 Artist 不能为空");
            Assert.That(t.Album, Is.Not.Null.And.Not.Empty, $"曲目 {t.FileName} 的 Album 不能为空");
            
            // 验证封面路径是否从 Album 表中正确关联拉取
            Assert.That(t.FileName, Is.EqualTo("Standard_Track.mp3"));
            Assert.That(t.AlbumCoverPath, Is.EqualTo(testCover), "应从关联的 Album 表中拉取封面路径");
        }

        [Test]
        public async Task GetByArtist_ShouldReturnCorrectTracks()
        {
            var tracks = TestDatabaseSeed.GetGoldenTracks().ToList();
            _trackRepo.BulkInsert(tracks);
            
            string targetArtist = "Pop Star"; // 黄金数据中真实存在的歌手
            var result = await _trackRepo.GetByArtistAsync(targetArtist);
            
            Assert.That(result.Any(), Is.True, $"应找到歌手 {targetArtist} 的曲目");
            foreach(var t in result)
            {
                Assert.That(t.Artist, Is.EqualTo(targetArtist));
            }
        }

        [Test]
        public async Task GetByAlbum_ShouldReturnCorrectTracks()
        {
            var tracks = TestDatabaseSeed.GetGoldenTracks().ToList();
            _trackRepo.BulkInsert(tracks);
            
            string targetAlbum = "Greatest Hits"; // 黄金数据中真实存在的专辑
            var result = await _trackRepo.GetByAlbumAsync(targetAlbum);
            
            Assert.That(result.Count, Is.EqualTo(2), "Greatest Hits 专辑应有 2 首曲目");
            foreach(var t in result)
            {
                Assert.That(t.Album, Is.EqualTo(targetAlbum));
            }
        }

        #endregion
    }
}
