using System;
using System.Data;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class ArtistCoverTests
    {
        private DatabaseHelper _dbHelper;
        private TrackRepository _trackRepo;
        private string _testDbPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDBs");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            _testDbPath = Path.Combine(tempDir, $"ArtistTest_{Guid.NewGuid()}.db");
            _dbHelper = new DatabaseHelper(_testDbPath);
            _dbHelper.InitializeDatabase();
            _trackRepo = new TrackRepository(_testDbPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
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
            using (var db = _dbHelper.GetConnection())
            {
                db.Execute("DELETE FROM Tracks;");
                db.Execute("DELETE FROM Albums;");
                db.Execute("DELETE FROM Artists;");
            }
        }

        [Test]
        public void ArtistCover_FirstScan_ShouldSetCover()
        {
            var track = new MusicTrack 
            { 
                FileName = "song1.mp3", 
                Artist = "Artist A", 
                Album = "Album 1", 
                CoverPath = "path/to/cover1.jpg",
                TotalSamples = 1000
            };

            _trackRepo.Save(track);

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist A'");
                Assert.That(artistCover, Is.EqualTo("path/to/cover1.jpg"), "First scan should set the artist cover.");
            }
        }

        [Test]
        public void ArtistCover_SecondScanDifferentCover_ShouldMaintainFirstCover()
        {
            var track1 = new MusicTrack 
            { 
                FileName = "song1.mp3", 
                Artist = "Artist A", 
                Album = "Album 1", 
                CoverPath = "path/to/cover1.jpg",
                TotalSamples = 1000
            };

            var track2 = new MusicTrack 
            { 
                FileName = "song2.mp3", 
                Artist = "Artist A", 
                Album = "Album 2", 
                CoverPath = "path/to/cover2.jpg",
                TotalSamples = 2000
            };

            _trackRepo.Save(track1);
            _trackRepo.Save(track2);

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist A'");
                Assert.That(artistCover, Is.EqualTo("path/to/cover1.jpg"), "Second scan with different cover should not overwrite the first artist cover.");
            }
        }

        [Test]
        public void ArtistCover_EmptyInitiallyThenScan_ShouldUpdateCover()
        {
            var track1 = new MusicTrack 
            { 
                FileName = "song1.mp3", 
                Artist = "Artist A", 
                Album = "Album 1", 
                CoverPath = null,
                TotalSamples = 1000
            };

            var track2 = new MusicTrack 
            { 
                FileName = "song2.mp3", 
                Artist = "Artist A", 
                Album = "Album 2", 
                CoverPath = "path/to/cover2.jpg",
                TotalSamples = 2000
            };

            _trackRepo.Save(track1);
            
            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist A'");
                Assert.That(string.IsNullOrEmpty(artistCover), Is.True, "Initial scan with no cover should leave artist cover empty.");
            }

            _trackRepo.Save(track2);

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist A'");
                Assert.That(artistCover, Is.EqualTo("path/to/cover2.jpg"), "Subsequent scan with cover should fill the empty artist cover.");
            }
        }

        [Test]
        public void BulkInsert_ShouldSetArtistCover()
        {
            string realPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bulk_test.jpg");
            File.WriteAllText(realPath, "fake content");

            var tracks = new[]
            {
                new MusicTrack { FileName = "s1.mp3", Artist = "Artist B", Album = "Alb 1", CoverPath = realPath, TotalSamples = 100 },
                new MusicTrack { FileName = "s2.mp3", Artist = "Artist B", Album = "Alb 2", CoverPath = "another.jpg", TotalSamples = 200 }
            };

            _trackRepo.BulkInsert(tracks);

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist B'");
                Assert.That(artistCover, Is.EqualTo(realPath), "BulkInsert should also correctly set and respect the first artist cover.");
            }

            if (File.Exists(realPath)) File.Delete(realPath);
        }
        [Test]
        public void GetAll_ShouldReturnArtistCoverPath()
        {
            var track = new MusicTrack 
            { 
                FileName = "song1.mp3", 
                Artist = "Artist C", 
                Album = "Album 1", 
                CoverPath = "track_cover.jpg",
                TotalSamples = 1000
            };

            _trackRepo.Save(track);

            var tracks = _trackRepo.GetAll().ToList();
            var savedTrack = tracks.FirstOrDefault(t => t.Artist == "Artist C");

            Assert.That(savedTrack, Is.Not.Null);
            Assert.That(savedTrack.ArtistCoverPath, Is.EqualTo("track_cover.jpg"), "The loaded track should include the artist's cover path.");
            Assert.That(savedTrack.AlbumCoverPath, Is.EqualTo("track_cover.jpg"), "The loaded track should include the album's cover path.");
        }

        [Test]
        public void ArtistCover_EmptyString_ShouldBeUpdated()
        {
            // 模拟数据库中已存在空字符串路径的艺术家
            using (var db = _dbHelper.GetConnection())
            {
                db.Execute("INSERT INTO Artists (Name, CoverPath) VALUES ('Artist Empty', '')");
                db.Execute("INSERT INTO Albums (Name, CoverPath) VALUES ('Album Empty', '')");
            }

            // 创建一个真实的临时文件以绕过物理校验
            string realPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_cover.jpg");
            File.WriteAllText(realPath, "fake image content");

            var track = new MusicTrack 
            { 
                FileName = "song_new.mp3", 
                Artist = "Artist Empty", 
                Album = "Album Empty", 
                CoverPath = realPath,
                TotalSamples = 5000
            };

            _trackRepo.Save(track);

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist Empty'");
                var albumCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Albums WHERE Name = 'Album Empty'");
                
                Assert.That(artistCover, Is.EqualTo(realPath), "Artist cover should be updated even if it was an empty string.");
                Assert.That(albumCover, Is.EqualTo(realPath), "Album cover should be updated even if it was an empty string.");
            }
            
            if (File.Exists(realPath)) File.Delete(realPath);
        }

        [Test]
        public void RepairMissingCategoryCovers_ShouldBackfill()
        {
            // 创建一个真实的临时文件以绕过物理校验
            string realPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repair_test.jpg");
            File.WriteAllText(realPath, "fake image content");

            // 准备数据：有曲目封面，但分类表封面为空
            using (var db = _dbHelper.GetConnection())
            {
                db.Execute("INSERT INTO Artists (Name, CoverPath) VALUES ('Repair Artist', '')");
                db.Execute("INSERT INTO Albums (Name, CoverPath) VALUES ('Repair Album', NULL)");
                
                var artistId = db.ExecuteScalar<int>("SELECT Id FROM Artists WHERE Name='Repair Artist'");
                var albumId = db.ExecuteScalar<int>("SELECT Id FROM Albums WHERE Name='Repair Album'");
                db.Execute("INSERT INTO Tracks (FileName, FilePath, AlbumId, ArtistId, CoverPath, TotalSamples) VALUES ('s1.mp3', 'p1', @Aid, @Rid, @Path, 100)", new { Aid = albumId, Rid = artistId, Path = realPath });
            }

            // 执行修复
            _dbHelper.RepairMissingCategoryCovers();

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Repair Artist'");
                var albumCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Albums WHERE Name = 'Repair Album'");
                
                Assert.That(artistCover, Is.EqualTo(realPath), "Repair should backfill artist cover.");
                Assert.That(albumCover, Is.EqualTo(realPath), "Repair should backfill album cover.");
            }

            if (File.Exists(realPath)) File.Delete(realPath);
        }

        [Test]
        public void Repair_UnknownCategory_ShouldNotBackfill()
        {
            // 场景：Unknown 专辑里的歌曲有封面，修复时不应同步给 Unknown 专辑/歌手
            string realPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unknown_test.jpg");
            File.WriteAllText(realPath, "fake image");

            using (var db = _dbHelper.GetConnection())
            {
                // 手动创建 Unknown 占位符
                db.Execute("INSERT INTO Artists (Name, CoverPath) VALUES ('Unknown Artist', NULL)");
                db.Execute("INSERT INTO Albums (Name, CoverPath) VALUES ('Unknown Album', NULL)");
                
                var artistId = db.ExecuteScalar<int>("SELECT Id FROM Artists WHERE Name='Unknown Artist'");
                var albumId = db.ExecuteScalar<int>("SELECT Id FROM Albums WHERE Name='Unknown Album'");
                
                // 插入一个带有封面的 Unknown 歌曲
                db.Execute("INSERT INTO Tracks (FileName, FilePath, AlbumId, ArtistId, CoverPath, TotalSamples) VALUES ('unknown.mp3', 'p', @Aid, @Rid, @Path, 100)", 
                    new { Aid = albumId, Rid = artistId, Path = realPath });
            }

            _dbHelper.RepairMissingCategoryCovers();

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Unknown Artist'");
                var albumCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Albums WHERE Name = 'Unknown Album'");
                
                Assert.That(string.IsNullOrEmpty(artistCover), Is.True, "Unknown Artist should NEVER get an automatic cover backfill.");
                Assert.That(string.IsNullOrEmpty(albumCover), Is.True, "Unknown Album should NEVER get an automatic cover backfill.");
            }

            if (File.Exists(realPath)) File.Delete(realPath);
        }
    }
}
