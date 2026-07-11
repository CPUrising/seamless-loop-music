using System;
using System.IO;
using System.Linq;
using Dapper;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsSyncPersistenceTests
    {
        private string _dbPath;
        private DatabaseHelper _database;
        private PlaybackStatisticsSyncRepository _repository;

        [SetUp]
        public void SetUp() { _dbPath = Path.Combine(Path.GetTempPath(), "PlaybackSync_" + Guid.NewGuid().ToString("N") + ".db"); _database = new DatabaseHelper(_dbPath); _database.InitializeDatabase(); _repository = new PlaybackStatisticsSyncRepository(_dbPath); }
        [TearDown]
        public void TearDown() { if (File.Exists(_dbPath)) File.Delete(_dbPath); }

        [Test]
        public void InitializeDatabase_CreatesSyncTablesAndIndexes()
        {
            using (var db = _database.GetConnection())
            {
                var tables = db.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToList();
                foreach (var name in new[] { "PlaybackSyncDevices", "PlaybackSyncSongs", "PlaybackSyncContributions", "PlaybackSyncDailyBuckets", "PlaybackSyncTombstones", "PlaybackStatisticsSettlements" }) Assert.That(tables, Does.Contain(name));
                var indexes = db.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();
                foreach (var name in new[] { "idx_playbacksyncsongs_localtrackid", "idx_playbacksynccontributions_device_generation", "idx_playbacksyncbuckets_localdate_songid", "idx_playbacksyncbuckets_songid_localdate", "idx_playbacksynctombstones_device_generation" }) Assert.That(indexes, Does.Contain(name));
            }
        }

        [Test]
        public void EnsureSchema_UpgradesLegacyUniqueLocalTrackIndex()
        {
            using (var db = _database.GetConnection())
            {
                db.Execute("DROP INDEX IF EXISTS idx_playbacksyncsongs_localtrackid");
                db.Execute("CREATE UNIQUE INDEX idx_playbacksyncsongs_localtrackid_unique ON PlaybackSyncSongs(LocalTrackId) WHERE LocalTrackId IS NOT NULL");
                PlaybackStatisticsSyncSchema.EnsureSchema(db);

                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_playbacksyncsongs_localtrackid_unique'"), Is.EqualTo(0));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_playbacksyncsongs_localtrackid'"), Is.EqualTo(1));
            }

            var trackId = InsertTrack("upgrade.mp3", 1000, 1);
            var first = Song("upgrade.mp3");
            var second = _repository.EnsureSong(new PlaybackSyncSong { FileName = "upgrade.mp3", DurationMs = 1001 });
            using (var db = _database.GetConnection())
            {
                Assert.DoesNotThrow(() => db.Execute("UPDATE PlaybackSyncSongs SET LocalTrackId=@trackId WHERE SongId IN (@first,@second)", new { trackId, first = first.SongId, second = second.SongId }));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSyncSongs WHERE LocalTrackId=@trackId", new { trackId }), Is.EqualTo(2));
            }
        }

        [Test]
        public void Constraints_RejectInvalidRowsAndOrphanBuckets()
        {
            using (var db = _database.GetConnection())
            {
                Assert.That(() => db.Execute("INSERT INTO PlaybackSyncDevices VALUES ('bad',-1,NULL,0,'linux',1,0)"), Throws.InstanceOf<Exception>());
                Assert.That(() => db.Execute("INSERT INTO PlaybackSyncDailyBuckets VALUES (1,'missing',0,'2026-02-30',1)"), Throws.InstanceOf<Exception>());
                Register("target"); Register("actor");
                Assert.That(() => db.Execute("INSERT INTO PlaybackSyncTombstones VALUES ('target',0,'wrong',0,'actor','test')"), Throws.InstanceOf<Exception>());
            }
        }

        [Test]
        public void EnsureSong_IsExactKeyIdempotentAndDoesNotFuzzyMerge()
        {
            var first = _repository.EnsureSong(new PlaybackSyncSong { FileName = @"C:\Music\Same.MP3", DurationMs = 245012 });
            var again = _repository.EnsureSong(new PlaybackSyncSong { FileName = "same.mp3", DurationMs = 245012, ContentHash = "b" });
            var distinct = _repository.EnsureSong(new PlaybackSyncSong { FileName = "same.mp3", DurationMs = 245100 });
            Assert.That(again.SongId, Is.EqualTo(first.SongId)); Assert.That(distinct.SongId, Is.Not.EqualTo(first.SongId));
        }

        [Test]
        public void EnsureSong_MergesTotalSamplesCommutativelyAndValidatesNullableValues()
        {
            var nullThenValue = _repository.EnsureSong(new PlaybackSyncSong { FileName = "samples.mp3", DurationMs = 1000 });
            var valueAfterNull = _repository.EnsureSong(new PlaybackSyncSong { FileName = "samples.mp3", DurationMs = 1000, TotalSamples = 10 });
            Assert.That(valueAfterNull.TotalSamples, Is.EqualTo(10));
            Assert.That(() => _repository.EnsureSong(new PlaybackSyncSong { FileName = "invalid.mp3", DurationMs = 1000, TotalSamples = -1 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            var otherPath = Path.Combine(Path.GetTempPath(), "PlaybackSync_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var database = new DatabaseHelper(otherPath); database.InitializeDatabase(); var repository = new PlaybackStatisticsSyncRepository(otherPath);
                repository.EnsureSong(new PlaybackSyncSong { FileName = "samples.mp3", DurationMs = 1000, TotalSamples = 10 });
                var valueAfterValue = repository.EnsureSong(new PlaybackSyncSong { FileName = "samples.mp3", DurationMs = 1000, TotalSamples = 5 });
                Assert.That(valueAfterValue.TotalSamples, Is.EqualTo(10)); Assert.That(nullThenValue.SongId, Is.EqualTo(valueAfterNull.SongId));
            }
            finally { if (File.Exists(otherPath)) File.Delete(otherPath); }
        }

        [Test]
        public void Constraints_AllowDuplicateLocalTrackLinksAndRejectReferencedDeviceDeletion()
        {
            var trackId = InsertTrack("one.mp3", 1, 1); var first = _repository.EnsureSong(new PlaybackSyncSong { FileName = "one.mp3", DurationMs = 1, LocalTrackId = trackId }); var second = Song("two.mp3");
            using (var db = _database.GetConnection())
            {
                Assert.DoesNotThrow(() => db.Execute("UPDATE PlaybackSyncSongs SET LocalTrackId=@trackId WHERE SongId=@songId", new { trackId, songId = second.SongId }));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSyncSongs WHERE LocalTrackId=@trackId", new { trackId }), Is.EqualTo(2));
            }
            Register("contribution"); Register("tombstoned"); Register("tombstoner"); Register("settlement");
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = first.SongId, DeviceId = "contribution", Generation = 0, UpdatedAtUtcMs = 1 });
            _repository.InsertTombstone(new PlaybackSyncTombstone { DeviceId = "tombstoned", Generation = 0, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = "tombstoner", Reason = "test" });
            _repository.RecordSettlement(new PlaybackSyncSettlement { SettlementEventId = "settlement", SongId = first.SongId, DeviceId = "settlement", Generation = 0, AppliedAtUtcMs = 1, SourceKind = "test" }, 0, null, null, null);
            using (var db = _database.GetConnection()) foreach (var deviceId in new[] { "contribution", "tombstoned", "tombstoner", "settlement" }) Assert.That(() => db.Execute("DELETE FROM PlaybackSyncDevices WHERE DeviceId=@deviceId", new { deviceId }), Throws.InstanceOf<Exception>());
        }

        [Test]
        public void EnsureSongBoundToTrack_RebindsExactSongAndLeavesOldTrackUnclaimed()
        {
            var oldTrack = InsertTrack("rebind.mp3", 1000, 1); var targetTrack = InsertTrack("rebind.mp3", 1000, 2);
            var song = _repository.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "rebind.mp3", DurationMs = 1000 }, oldTrack);

            Assert.DoesNotThrow(() => _repository.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "rebind.mp3", DurationMs = 1000 }, targetTrack));

            var state = _repository.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == song.SongId).LocalTrackId, Is.EqualTo(targetTrack));
            using (var db = _database.GetConnection()) Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSyncSongs WHERE LocalTrackId=@trackId", new { trackId = oldTrack }), Is.EqualTo(0));
        }

        [Test]
        public void Relink_LeavesExactAndFuzzyAmbiguityUnresolved()
        {
            InsertTrack("ambiguous-exact.mp3", 1000, 10); InsertTrack("ambiguous-exact.mp3", 1000, 11);
            InsertTrack("ambiguous-fuzzy.mp3", 1000, 20); InsertTrack("ambiguous-fuzzy.mp3", 1100, 21);
            var exact = Song("ambiguous-exact.mp3"); var fuzzy = _repository.EnsureSong(new PlaybackSyncSong { FileName = "ambiguous-fuzzy.mp3", DurationMs = 1050 });

            Assert.That(_repository.RelinkSongs(), Is.EqualTo(0));

            var state = _repository.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == exact.SongId).LocalTrackId, Is.Null);
            Assert.That(state.Songs.Single(x => x.SongId == fuzzy.SongId).LocalTrackId, Is.Null);
        }

        [Test]
        public void Relink_ExactAmbiguityIsTerminalEvenWhenSamplesWouldBeUnique()
        {
            InsertTrack("ambiguous-precedence.mp3", 1000, 20000);
            InsertTrack("ambiguous-precedence.mp3", 1000, 30000);
            var song = _repository.EnsureSong(new PlaybackSyncSong
            {
                FileName = "ambiguous-precedence.mp3",
                DurationMs = 1000,
                TotalSamples = 20000
            });

            Assert.That(_repository.RelinkSongs(), Is.EqualTo(0));
            Assert.That(_repository.LoadState().Songs.Single(x => x.SongId == song.SongId).LocalTrackId, Is.Null);
        }

        [Test]
        public void Relink_UsesSampleDurationAndFallbackTiersWithoutConsumingTracks()
        {
            InsertTrack("tier-exact.mp3", 1000, 10);
            InsertTrack("tier-exact.mp3", 1000, 20);
            var exact = Song("tier-exact.mp3");

            var sampleBoundaryTrack = InsertTrack("tier-sample-boundary.mp3", 1000, 100000);
            var sampleBoundary = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-sample-boundary.mp3", DurationMs = 5000, TotalSamples = 110000 });

            InsertTrack("tier-sample-multiple.mp3", 1000, 100000);
            InsertTrack("tier-sample-multiple.mp3", 2000, 110000);
            var sampleMultiple = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-sample-multiple.mp3", DurationMs = 5000, TotalSamples = 105000 });

            InsertTrack("tier-sample-missing.mp3", 1000, 100000);
            InsertTrack("tier-sample-missing.mp3", 2000, 200000);
            var sampleMissing = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-sample-missing.mp3", DurationMs = 5000 });

            var plusTrack = InsertTrack("tier-duration-plus.mp3", 1000, 300000);
            var plus = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-duration-plus.mp3", DurationMs = 1200 });
            var minusTrack = InsertTrack("tier-duration-minus.mp3", 1000, 400000);
            var minus = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-duration-minus.mp3", DurationMs = 800 });

            InsertTrack("tier-duration-over.mp3", 1000, 500000);
            InsertTrack("tier-duration-over.mp3", 2000, 600000);
            var over = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-duration-over.mp3", DurationMs = 1201 });

            var fallbackTrack = InsertTrack("tier-fallback.mp3", 1000, 700000);
            var fallback = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-fallback.mp3", DurationMs = 5000 });

            var stableTrack = InsertTrack("tier-stable.mp3", 1000, 800000);
            var stable = _repository.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "tier-stable.mp3", DurationMs = 1000 }, stableTrack);
            var stableNewIdentity = _repository.EnsureSong(new PlaybackSyncSong { FileName = "tier-stable.mp3", DurationMs = 1001 });

            Assert.That(_repository.RelinkSongs(), Is.EqualTo(5));
            var state = _repository.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == exact.SongId).LocalTrackId, Is.Null, "ambiguous exact tier is terminal");
            Assert.That(state.Songs.Single(x => x.SongId == sampleBoundary.SongId).LocalTrackId, Is.EqualTo(sampleBoundaryTrack));
            Assert.That(state.Songs.Single(x => x.SongId == sampleMultiple.SongId).LocalTrackId, Is.Null, "multiple sample candidates are terminal");
            Assert.That(state.Songs.Single(x => x.SongId == sampleMissing.SongId).LocalTrackId, Is.Null, "missing samples do not enter the sample tier");
            Assert.That(state.Songs.Single(x => x.SongId == plus.SongId).LocalTrackId, Is.EqualTo(plusTrack));
            Assert.That(state.Songs.Single(x => x.SongId == minus.SongId).LocalTrackId, Is.EqualTo(minusTrack));
            Assert.That(state.Songs.Single(x => x.SongId == over.SongId).LocalTrackId, Is.Null, "+/-201 does not enter the duration tier");
            Assert.That(state.Songs.Single(x => x.SongId == fallback.SongId).LocalTrackId, Is.EqualTo(fallbackTrack));
            Assert.That(state.Songs.Single(x => x.SongId == stable.SongId).LocalTrackId, Is.EqualTo(stableTrack));
            Assert.That(state.Songs.Single(x => x.SongId == stableNewIdentity.SongId).LocalTrackId, Is.EqualTo(stableTrack));
        }

        [Test]
        public void EnsureSongBoundToTrack_PreoccupiedTargetLeavesExactlyOneBinding()
        {
            var track = InsertTrack("occupied.mp3", 1000, 1);
            var fuzzy = _repository.EnsureSong(new PlaybackSyncSong { FileName = "occupied.mp3", DurationMs = 900 });
            using (var db = _database.GetConnection()) db.Execute("UPDATE PlaybackSyncSongs SET LocalTrackId=@trackId WHERE SongId=@songId", new { trackId = track, songId = fuzzy.SongId });
            var exact = _repository.EnsureSong(new PlaybackSyncSong { FileName = "occupied.mp3", DurationMs = 1000 });

            Assert.DoesNotThrow(() => _repository.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "occupied.mp3", DurationMs = 1000 }, track));

            var state = _repository.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == exact.SongId).LocalTrackId, Is.EqualTo(track));
            Assert.That(state.Songs.Single(x => x.SongId == fuzzy.SongId).LocalTrackId, Is.EqualTo(track));
            Assert.That(state.Songs.Count(x => x.LocalTrackId == track), Is.EqualTo(2));
        }

        [Test]
        public void TrackDeletion_UnlinksSyncSongAndPreservesStatistics()
        {
            var trackId = InsertTrack(); var song = _repository.EnsureSong(new PlaybackSyncSong { FileName = "track.mp3", DurationMs = 1000, LocalTrackId = trackId }); Register("device");
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = song.SongId, DeviceId = "device", Generation = 0, UpdatedAtUtcMs = 1, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-01", ListenMs = 2 } } });
            using (var db = _database.GetConnection()) db.Execute("DELETE FROM Tracks WHERE Id=@trackId", new { trackId });
            var state = _repository.LoadState(); Assert.That(state.Songs.Single().LocalTrackId, Is.Null); Assert.That(state.Contributions, Has.Count.EqualTo(1)); Assert.That(state.Contributions.Single().DailyBuckets, Has.Count.EqualTo(1));
        }

        [Test]
        public void MergeContribution_UsesMaxBucketsMinFirstAndMaxLast()
        {
            var song = Song(); Register("device");
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = song.SongId, DeviceId = "device", Generation = 0, UndatedListenMs = 8, FirstPlayedAtUtcMs = 20, LastPlayedAtUtcMs = 30, UpdatedAtUtcMs = 30, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-01", ListenMs = 10 } } });
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = song.SongId, DeviceId = "device", Generation = 0, UndatedListenMs = 5, FirstPlayedAtUtcMs = 10, LastPlayedAtUtcMs = 40, UpdatedAtUtcMs = 40, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-01", ListenMs = 7 }, new PlaybackSyncDailyBucket { LocalDate = "2026-03-02", ListenMs = 3 } } });
            var value = _repository.LoadState().Contributions.Single(); Assert.That(value.UndatedListenMs, Is.EqualTo(8)); Assert.That(value.FirstPlayedAtUtcMs, Is.EqualTo(10)); Assert.That(value.LastPlayedAtUtcMs, Is.EqualTo(40)); Assert.That(value.DailyBuckets.Single(x => x.LocalDate == "2026-03-01").ListenMs, Is.EqualTo(10));
        }

        [Test]
        public void RecordSettlement_IsIdempotentAndSaturates()
        {
            var song = Song(); Register("device"); var first = Settlement("one", song.SongId);
            Assert.That(_repository.RecordSettlement(first, long.MaxValue - 1, "2026-03-01", 10, 20), Is.True); Assert.That(_repository.RecordSettlement(first, 9, "2026-03-01", 10, 20), Is.False);
            Assert.That(_repository.RecordSettlement(Settlement("two", song.SongId), 9, "2026-03-01", 5, 30), Is.True);
            var contribution = _repository.LoadState().Contributions.Single(); Assert.That(contribution.DailyBuckets.Single().ListenMs, Is.EqualTo(long.MaxValue)); Assert.That(contribution.FirstPlayedAtUtcMs, Is.EqualTo(5)); Assert.That(contribution.LastPlayedAtUtcMs, Is.EqualTo(30));
        }

        [Test]
        public void RecordSettlement_TombstonedGeneration_IsConsumedButNotCounted()
        {
            var song = Song(); Register("device"); Register("actor");
            _repository.InsertTombstone(new PlaybackSyncTombstone { DeviceId = "device", Generation = 0, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = "actor", Reason = "clear" });

            Assert.That(_repository.RecordSettlement(Settlement("tombstoned", song.SongId), 9, "2026-03-01", 10, 20), Is.True);
            Assert.That(_repository.RecordSettlement(Settlement("tombstoned", song.SongId), 9, "2026-03-01", 10, 20), Is.False);

            var state = _repository.LoadState();
            Assert.That(state.Contributions, Is.Empty);
            using (var db = _database.GetConnection())
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaybackStatisticsSettlements WHERE SettlementEventId='tombstoned'"), Is.EqualTo(1));
        }

        [Test]
        public void InsertTombstone_IsIdempotentAndSurvivesTrackDeletion()
        {
            var song = Song(); Register("target"); Register("actor"); var tombstone = new PlaybackSyncTombstone { DeviceId = "target", Generation = 0, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = "actor", Reason = "clear" };
            _repository.InsertTombstone(tombstone); _repository.InsertTombstone(tombstone); using (var db = _database.GetConnection()) db.Execute("DELETE FROM Tracks");
            Assert.That(_repository.LoadState().Tombstones, Has.Count.EqualTo(1)); Assert.That(song.SongId, Is.GreaterThan(0));
        }

        [Test]
        public void EnsureDevice_IsNonAuthoritativeAndOwnerAuthoredMergeAdvancesWithoutDecreasing()
        {
            var initial = _repository.EnsureDevice(new PlaybackSyncDevice { DeviceId = "device", CurrentGeneration = 2, DisplayName = "A", DisplayNameUpdatedAtUtcMs = 10, Platform = "windows", FirstSeenAtUtcMs = 10, LastSeenAtUtcMs = 20 });
            var merged = _repository.EnsureDevice(new PlaybackSyncDevice { DeviceId = "device", CurrentGeneration = 9, DisplayName = "B", DisplayNameUpdatedAtUtcMs = 10, Platform = "windows", FirstSeenAtUtcMs = 5, LastSeenAtUtcMs = 30 });
            Assert.That(merged.CurrentGeneration, Is.EqualTo(2)); Assert.That(merged.DisplayName, Is.EqualTo("B")); Assert.That(merged.FirstSeenAtUtcMs, Is.EqualTo(5)); Assert.That(merged.LastSeenAtUtcMs, Is.EqualTo(30));
            Assert.That(_repository.MergeOwnerAuthoredDeviceRegistration(new PlaybackSyncDevice { DeviceId = "device", CurrentGeneration = 3, DisplayNameUpdatedAtUtcMs = 9, Platform = "windows", FirstSeenAtUtcMs = 6, LastSeenAtUtcMs = 31 }).CurrentGeneration, Is.EqualTo(3));
            Assert.That(_repository.MergeOwnerAuthoredDeviceRegistration(new PlaybackSyncDevice { DeviceId = "device", CurrentGeneration = 1, DisplayNameUpdatedAtUtcMs = 9, Platform = "windows", FirstSeenAtUtcMs = 6, LastSeenAtUtcMs = 32 }).CurrentGeneration, Is.EqualTo(3)); Assert.That(initial.DeviceId, Is.EqualTo("device"));
        }

        private PlaybackSyncSong Song(string fileName = "track.mp3") => _repository.EnsureSong(new PlaybackSyncSong { FileName = fileName, DurationMs = 1000 });
        private void Register(string id) => _repository.EnsureDevice(new PlaybackSyncDevice { DeviceId = id, CurrentGeneration = 0, DisplayName = id, DisplayNameUpdatedAtUtcMs = 1, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });
        private PlaybackSyncSettlement Settlement(string id, long songId) => new PlaybackSyncSettlement { SettlementEventId = id, SongId = songId, DeviceId = "device", Generation = 0, AppliedAtUtcMs = 1, SourceKind = "test" };
        private int InsertTrack(string fileName = "track.mp3", long duration = 1000, long totalSamples = 1) { using (var db = _database.GetConnection()) return db.ExecuteScalar<int>("INSERT INTO Tracks (FileName,TotalSamples,DurationMs) VALUES (@fileName,@totalSamples,@duration); SELECT last_insert_rowid();", new { fileName, duration, totalSamples }); }
    }
}
