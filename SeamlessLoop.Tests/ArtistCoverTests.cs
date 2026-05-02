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
            var tracks = new[]
            {
                new MusicTrack { FileName = "s1.mp3", Artist = "Artist B", Album = "Alb 1", CoverPath = "c1.jpg", TotalSamples = 100 },
                new MusicTrack { FileName = "s2.mp3", Artist = "Artist B", Album = "Alb 2", CoverPath = "c2.jpg", TotalSamples = 200 }
            };

            _trackRepo.BulkInsert(tracks);

            using (var db = _dbHelper.GetConnection())
            {
                var artistCover = db.ExecuteScalar<string>("SELECT CoverPath FROM Artists WHERE Name = 'Artist B'");
                Assert.That(artistCover, Is.EqualTo("c1.jpg"), "BulkInsert should also correctly set and respect the first artist cover.");
            }
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
        }
    }
}
