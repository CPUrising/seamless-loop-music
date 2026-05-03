using System;
using System.Collections.Generic;
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
    public class GroupingAndFilteringTests
    {
        private DatabaseHelper _dbHelper;
        private TrackRepository _trackRepo;
        private string _testDbPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDBs");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            _testDbPath = Path.Combine(tempDir, $"GroupingTest_{Guid.NewGuid()}.db");
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
        public async Task GetByAlbumAsync_WithSameAlbumNameDifferentArtist_ShouldFilterCorrectly()
        {
            // 准备数据：两个艺术家都有名为 "Greatest Hits" 的专辑
            var track1 = new MusicTrack { FileName = "queen.mp3", Artist = "Queen", Album = "Greatest Hits", TotalSamples = 1000 };
            var track2 = new MusicTrack { FileName = "gnr.mp3", Artist = "Guns N' Roses", Album = "Greatest Hits", TotalSamples = 2000 };
            
            _trackRepo.Save(track1);
            _trackRepo.Save(track2);

            // 1. 测试只查 Queen 的 Greatest Hits
            var queenTracks = await _trackRepo.GetByAlbumAsync("Greatest Hits", "Queen");
            Assert.That(queenTracks.Count, Is.EqualTo(1));
            Assert.That(queenTracks[0].Artist, Is.EqualTo("Queen"));

            // 2. 测试只查 Guns N' Roses 的 Greatest Hits
            var gnrTracks = await _trackRepo.GetByAlbumAsync("Greatest Hits", "Guns N' Roses");
            Assert.That(gnrTracks.Count, Is.EqualTo(1));
            Assert.That(gnrTracks[0].Artist, Is.EqualTo("Guns N' Roses"));
        }

        [Test]
        public async Task GetByAlbumAsync_WithoutArtistName_ShouldReturnAllMatchingAlbums()
        {
            // 准备数据
            var track1 = new MusicTrack { FileName = "queen.mp3", Artist = "Queen", Album = "Greatest Hits", TotalSamples = 1000 };
            var track2 = new MusicTrack { FileName = "gnr.mp3", Artist = "Guns N' Roses", Album = "Greatest Hits", TotalSamples = 2000 };
            
            _trackRepo.Save(track1);
            _trackRepo.Save(track2);

            // 测试不传艺术家名，应该返回所有同名专辑的曲目
            var allTracks = await _trackRepo.GetByAlbumAsync("Greatest Hits");
            Assert.That(allTracks.Count, Is.EqualTo(2), "Should return all tracks from both artists when artist name is null.");
        }

        [Test]
        public async Task GetByAlbumAsync_WithNonExistentArtist_ShouldReturnEmpty()
        {
            var track = new MusicTrack { FileName = "song.mp3", Artist = "Artist A", Album = "Album 1", TotalSamples = 1000 };
            _trackRepo.Save(track);

            // 专辑名匹配但艺术家名不匹配
            var results = await _trackRepo.GetByAlbumAsync("Album 1", "NonExistent Artist");
            Assert.That(results, Is.Empty);
        }

        [Test]
        public async Task GroupingLogic_InRepository_ShouldMaintainIntegrity()
        {
            // 模拟 LibraryViewModel 里的分组逻辑
            var tracks = new List<MusicTrack>
            {
                new MusicTrack { Artist = "Artist A", Album = "Album 1", FileName = "s1.mp3", TotalSamples = 100 },
                new MusicTrack { Artist = "Artist A", Album = "Album 1", FileName = "s2.mp3", TotalSamples = 200 },
                new MusicTrack { Artist = "Artist B", Album = "Album 1", FileName = "s3.mp3", TotalSamples = 300 }
            };

            foreach (var t in tracks) _trackRepo.Save(t);

            var allSaved = await _trackRepo.GetAllAsync();
            
            // 验证按专辑+艺术家分组后的数量
            var groups = allSaved
                .Where(t => !string.IsNullOrEmpty(t.Album))
                .GroupBy(t => new { t.Album, t.Artist })
                .ToList();

            Assert.That(groups.Count, Is.EqualTo(2), "Should have 2 distinct Album+Artist groups.");
            
            var groupA = groups.FirstOrDefault(g => g.Key.Artist == "Artist A");
            Assert.That(groupA.Count(), Is.EqualTo(2));

            var groupB = groups.FirstOrDefault(g => g.Key.Artist == "Artist B");
            Assert.That(groupB.Count(), Is.EqualTo(1));
        }
    }
}
