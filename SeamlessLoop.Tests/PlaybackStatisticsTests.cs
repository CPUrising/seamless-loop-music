using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.UI.ViewModels;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsTests
    {
        private string _dbPath;
        private DatabaseHelper _database;
        private PlaybackStatisticsRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "PlaybackStatistics_" + Guid.NewGuid().ToString("N") + ".db");
            _database = new DatabaseHelper(_dbPath);
            _database.InitializeDatabase();
            _repository = new PlaybackStatisticsRepository(_dbPath);
        }

        [TearDown]
        public void TearDown() { if (File.Exists(_dbPath)) File.Delete(_dbPath); }

        [Test]
        public void InitializeDatabase_CreatesSegmentsAndIndexes()
        {
            using (var db = _database.GetConnection())
            {
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PlaybackSegments'"), Is.EqualTo(1));
                var indexes = db.Query<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='PlaybackSegments'").ToList();
                Assert.That(indexes, Does.Contain("idx_playbacksegments_startedatutcms_trackid"));
                Assert.That(indexes, Does.Contain("idx_playbacksegments_trackid_startedatutcms"));
            }
        }

        [Test]
        public async Task RecordPlaybackSegmentAsync_IsIdempotentAndRejectsInvalidDuration()
        {
            var trackId = InsertTrack("idempotent.mp3", "Idempotent");
            var start = UtcMs(new DateTime(2026, 3, 18, 10, 0, 0, DateTimeKind.Utc));
            await _repository.RecordPlaybackSegmentAsync("fixed-segment", trackId, start, 1000);
            await _repository.RecordPlaybackSegmentAsync("fixed-segment", trackId, start, 1000);
            using (var db = _database.GetConnection()) Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSegments"), Is.EqualTo(1));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await _repository.RecordPlaybackSegmentAsync("bad", trackId, start, 0));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await _repository.RecordPlaybackSegmentAsync("overflow", trackId, start, long.MaxValue));
        }

        [Test]
        public async Task GetTopTracksAsync_UsesDurationOverlapCapsNowAndIgnoresHistory()
        {
            var now = new DateTime(2026, 3, 18, 12, 0, 0, DateTimeKind.Local);
            var trackId = InsertTrack("overlap.mp3", "Overlap");
            var start = UtcMs(now.Date.AddMinutes(-1));
            await _repository.RecordPlaybackSegmentAsync("cross-day", trackId, start, 120000);
            using (var db = _database.GetConnection()) db.Execute("INSERT INTO PlaybackHistory (TrackId, PlayedAtUtc) VALUES (@TrackId, CURRENT_TIMESTAMP)", new { TrackId = trackId });
            var result = (await _repository.GetTopTracksAsync(PlaybackStatisticsPeriod.Day, 5, now)).Single();
            Assert.That(result.TotalDurationMs, Is.EqualTo(60000));
        }

        [Test]
        public async Task GetTopTracksAsync_AggregatesDayWeekMonthYearAndOrdersWithLimit()
        {
            var now = new DateTime(2026, 3, 18, 12, 0, 0, DateTimeKind.Local);
            var day = InsertTrack("day.mp3", "Day");
            var week = InsertTrack("week.mp3", "Week");
            var month = InsertTrack("month.mp3", "Month");
            var year = InsertTrack("year.mp3", "Year");
            await Add("day", day, now.AddMinutes(-2), 60000);
            await Add("week", week, new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Local), 2000);
            await Add("month", month, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local), 3000);
            await Add("year", year, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local), 4000);
            Assert.That((await _repository.GetTopTracksAsync(PlaybackStatisticsPeriod.Day, 10, now)).Sum(x => x.TotalDurationMs), Is.EqualTo(60000));
            Assert.That((await _repository.GetTopTracksAsync(PlaybackStatisticsPeriod.Week, 10, now)).Sum(x => x.TotalDurationMs), Is.EqualTo(62000));
            Assert.That((await _repository.GetTopTracksAsync(PlaybackStatisticsPeriod.Month, 10, now)).Sum(x => x.TotalDurationMs), Is.EqualTo(65000));
            Assert.That((await _repository.GetTopTracksAsync(PlaybackStatisticsPeriod.Year, 2, now)).Select(x => x.TrackId), Is.EqualTo(new[] { day, year }));
        }

        [Test]
        public async Task DeleteTrack_CascadesPlaybackSegments()
        {
            var trackId = InsertTrack("cascade.mp3", "Cascade");
            await Add("cascade", trackId, DateTime.Now, 1000);
            using (var db = _database.GetConnection())
            {
                db.Execute("DELETE FROM Tracks WHERE Id=@Id", new { Id = trackId });
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSegments WHERE TrackId=@Id", new { Id = trackId }), Is.EqualTo(0));
            }
        }

        [Test]
        public async Task ClearAllAsync_ClearsSegmentsAndHistoryButKeepsTracks()
        {
            var trackId = InsertTrack("clear.mp3", "Clear");
            await _repository.RecordPlaybackSegmentAsync("clear-segment", trackId, UtcMs(DateTime.UtcNow), 1000);
            using (var db = _database.GetConnection())
                db.Execute("INSERT INTO PlaybackHistory (TrackId, PlayedAtUtc) VALUES (@TrackId, CURRENT_TIMESTAMP);", new { TrackId = trackId });

            var affected = await _repository.ClearAllAsync();

            using (var db = _database.GetConnection())
            {
                Assert.That(affected, Is.EqualTo(2));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSegments"), Is.EqualTo(0));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackHistory"), Is.EqualTo(0));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM Tracks WHERE Id=@trackId", new { trackId }), Is.EqualTo(1));
            }
        }

        [Test]
        public void TrackCoverResolver_UsesSharedTrackAlbumArtistFallbackOrder()
        {
            var track = TempImage("track"); var album = TempImage("album"); var artist = TempImage("artist");
            try
            {
                var musicTrack = new MusicTrack { CoverPath = track, AlbumCoverPath = album, ArtistCoverPath = artist };
                Assert.That(musicTrack.EffectiveCoverPath, Is.EqualTo(TrackCoverResolver.Resolve(track, album, artist)));
                Assert.That(TrackCoverResolver.Resolve("missing", album, artist), Is.EqualTo(album));
                Assert.That(TrackCoverResolver.Resolve("missing", "missing", artist), Is.EqualTo(artist));
            }
            finally { File.Delete(track); File.Delete(album); File.Delete(artist); }
        }

        [Test]
        public async Task GetTopTracksAsync_ReturnsAllCoverPathSources()
        {
            var trackId = InsertTrack("covers.mp3", "Covers");
            using (var db = _database.GetConnection())
            {
                db.Execute("UPDATE Tracks SET CoverPath='track-cover', AlbumId=(SELECT Id FROM Albums WHERE Name='cover-album'), ArtistId=(SELECT Id FROM Artists WHERE Name='cover-artist') WHERE Id=@trackId; INSERT OR IGNORE INTO Albums (Name, CoverPath) VALUES ('cover-album', 'album-cover'); INSERT OR IGNORE INTO Artists (Name, CoverPath) VALUES ('cover-artist', 'artist-cover'); UPDATE Tracks SET AlbumId=(SELECT Id FROM Albums WHERE Name='cover-album'), ArtistId=(SELECT Id FROM Artists WHERE Name='cover-artist') WHERE Id=@trackId;", new { trackId });
            }
            await Add("covers", trackId, DateTime.Now, 1000);
            var item = (await _repository.GetTopTracksAsync(PlaybackStatisticsPeriod.All, 5, DateTime.Now)).Single();
            Assert.That(item.TrackCoverPath, Is.EqualTo("track-cover"));
            Assert.That(item.AlbumCoverPath, Is.EqualTo("album-cover"));
            Assert.That(item.ArtistCoverPath, Is.EqualTo("artist-cover"));
        }

        [Test]
        public void PlaybackStatisticsTrack_FormatDuration_HandlesBoundaries()
        {
            var underSecond = PlaybackStatisticsTrack.FormatDuration(1);
            var fiftyNineSeconds = PlaybackStatisticsTrack.FormatDuration(59000);
            var oneMinute = PlaybackStatisticsTrack.FormatDuration(60000);
            var fiftyNineMinutesFiftyNineSeconds = PlaybackStatisticsTrack.FormatDuration((59 * 60 + 59) * 1000L);
            var oneHour = PlaybackStatisticsTrack.FormatDuration(60 * 60 * 1000L);

            Assert.That(underSecond, Does.Contain("1"));
            Assert.That(fiftyNineSeconds, Does.Contain("59"));
            Assert.That(oneMinute, Does.Contain("1"));
            Assert.That(fiftyNineMinutesFiftyNineSeconds, Does.Contain("59"));
            Assert.That(oneHour, Does.Contain("1"));
            Assert.That(oneMinute, Is.Not.EqualTo(fiftyNineSeconds));
            Assert.That(oneHour, Is.Not.EqualTo(fiftyNineMinutesFiftyNineSeconds));
        }

        [Test]
        public void PlaybackStatisticsTrack_CalculateBarPercent_NormalizesAndHandlesZero()
        {
            Assert.That(PlaybackStatisticsTrack.CalculateBarPercent(1000, 1000), Is.EqualTo(100));
            Assert.That(PlaybackStatisticsTrack.CalculateBarPercent(500, 1000), Is.EqualTo(50));
            Assert.That(PlaybackStatisticsTrack.CalculateBarPercent(1, 1000), Is.EqualTo(10));
            Assert.That(PlaybackStatisticsTrack.CalculateBarPercent(0, 1000), Is.EqualTo(0));
            Assert.That(PlaybackStatisticsTrack.CalculateBarPercent(1000, 0), Is.EqualTo(0));
        }

        [Test]
        public void CreateRankedTracks_ReturnsTopFiveFullRankingAndSummaryForSevenItems()
        {
            var groups = PlaybackStatisticsViewModel.CreateRankedTracks(Enumerable.Range(1, 7)
                .Select(index => new PlaybackStatisticItem
                {
                    TrackId = index,
                    Title = "Track " + index,
                    TotalDurationMs = (8 - index) * 1000L
                }));

            Assert.That(groups.TopTracks, Has.Count.EqualTo(5));
            Assert.That(groups.MostListenedTracks, Has.Count.EqualTo(7));
            Assert.That(groups.MostListenedTracks.Select(track => track.Rank), Is.EqualTo(Enumerable.Range(1, 7)));
            Assert.That(groups.TopTracks.First().TrackId, Is.EqualTo(groups.MostListenedTracks.First().TrackId));
            Assert.That(groups.TotalListeningDurationMs, Is.EqualTo(28000));
            Assert.That(groups.TrackedTrackCount, Is.EqualTo(7));
        }

        [Test]
        public async Task PlaybackStatisticsOutbox_RoundTripsCustomPathAndFiltersInvalidSegments()
        {
            var path = NewOutboxPath();
            var outbox = new PlaybackStatisticsOutbox(path);
            try
            {
                await outbox.SaveAsync(new[]
                {
                    Segment("one"), new PlaybackSegment("", 1, 1, 1), new PlaybackSegment("bad-track", 0, 1, 1),
                    new PlaybackSegment("bad-overflow", 1, long.MaxValue, 1)
                });
                var loaded = outbox.Load();
                Assert.That(loaded.Count, Is.EqualTo(1));
                Assert.That(loaded[0].SegmentId, Is.EqualTo("one"));
            }
            finally { DeleteOutboxDirectory(path); }
        }

        [Test]
        public async Task PlaybackStatisticsOutbox_DeletesEmptyAndAtomicallyOverwrites()
        {
            var path = NewOutboxPath();
            var outbox = new PlaybackStatisticsOutbox(path);
            try
            {
                await outbox.SaveAsync(new[] { Segment("old") });
                await outbox.SaveAsync(new[] { Segment("new") });
                Assert.That(outbox.Load().Select(x => x.SegmentId), Is.EqualTo(new[] { "new" }));
                Assert.That(File.Exists(path + ".bak"), Is.False);
                await outbox.SaveAsync(new PlaybackSegment[0]);
                Assert.That(File.Exists(path), Is.False);
            }
            finally { DeleteOutboxDirectory(path); }
        }

        [Test]
        public void PlaybackStatisticsOutbox_IsolatesCorruptJson()
        {
            var path = NewOutboxPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "not-json");
                Assert.That(new PlaybackStatisticsOutbox(path).Load(), Is.Empty);
                Assert.That(File.Exists(path), Is.False);
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), "PlaybackSegments.pending.json.*.corrupt").Length, Is.EqualTo(1));
            }
            finally { DeleteOutboxDirectory(path); }
        }

        [Test]
        public async Task PlaybackStatisticsOutbox_RestoresBackupWhenPrimaryIsMissing()
        {
            var path = NewOutboxPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                await outbox.SaveAsync(new[] { Segment("backup") });
                File.Move(path, path + ".bak");
                Assert.That(outbox.Load().Select(x => x.SegmentId), Is.EqualTo(new[] { "backup" }));
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(path + ".bak"), Is.False);
            }
            finally { DeleteOutboxDirectory(path); }
        }

        [Test]
        public async Task PlaybackStatisticsOutbox_RestoresValidBackupWhenPrimaryIsCorrupt()
        {
            var path = NewOutboxPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                await outbox.SaveAsync(new[] { Segment("backup") });
                File.Move(path, path + ".bak");
                File.WriteAllText(path, "not-json");
                Assert.That(outbox.Load().Select(x => x.SegmentId), Is.EqualTo(new[] { "backup" }));
                Assert.That(File.Exists(path), Is.True);
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), "PlaybackSegments.pending.json.*.corrupt").Length, Is.EqualTo(1));
            }
            finally { DeleteOutboxDirectory(path); }
        }

        [Test]
        public async Task PlaybackStatisticsOutbox_RestoresBackupWhenPrimaryContainsInvalidSegment()
        {
            var path = NewOutboxPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                await outbox.SaveAsync(new[] { Segment("backup") });
                var backupJson = File.ReadAllText(path);
                File.Move(path, path + ".bak");
                File.WriteAllText(path, "[{\"SegmentId\":\"invalid\",\"TrackId\":0,\"StartedAtUtcMs\":1000,\"DurationMs\":1000}]");

                Assert.That(outbox.Load().Select(x => x.SegmentId), Is.EqualTo(new[] { "backup" }));
                Assert.That(File.ReadAllText(path), Is.EqualTo(backupJson));
                Assert.That(File.Exists(path + ".bak"), Is.False);
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), "PlaybackSegments.pending.json.*.corrupt").Length, Is.EqualTo(1));
            }
            finally { DeleteOutboxDirectory(path); }
        }

        private async Task Add(string id, int trackId, DateTime start, long durationMs) { await _repository.RecordPlaybackSegmentAsync(id, trackId, UtcMs(start), durationMs); }
        private int InsertTrack(string fileName, string displayName)
        {
            using (var db = _database.GetConnection()) return db.ExecuteScalar<int>("INSERT INTO Tracks (FileName, DisplayName, TotalSamples) VALUES (@fileName, @displayName, 1); SELECT last_insert_rowid();", new { fileName, displayName });
        }
        private static long UtcMs(DateTime value) { return new DateTimeOffset(value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime()).ToUnixTimeMilliseconds(); }
        private static string TempImage(string name) { var path = Path.Combine(Path.GetTempPath(), name + Guid.NewGuid().ToString("N") + ".png"); File.WriteAllBytes(path, new byte[] { 0 }); return path; }
        private static PlaybackSegment Segment(string id) { return new PlaybackSegment(id, 1, 1000, 1000); }
        private static string NewOutboxPath() { return Path.Combine(Path.GetTempPath(), "PlaybackOutbox_" + Guid.NewGuid().ToString("N"), "PlaybackSegments.pending.json"); }
        private static void DeleteOutboxDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
