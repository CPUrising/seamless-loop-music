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
using seamless_loop_music.Services.Sync;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsLocalServiceTests
    {
        private string _path; private DatabaseHelper _db; private PlaybackStatisticsSyncRepository _repo; private PlaybackStatisticsLocalService _service;
        [SetUp] public void SetUp() { _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"); _db = new DatabaseHelper(_path); _db.InitializeDatabase(); _repo = new PlaybackStatisticsSyncRepository(_path); _service = new PlaybackStatisticsLocalService(_db, _repo); }
        [TearDown] public void TearDown() { if (File.Exists(_path)) File.Delete(_path); }

        [Test]
        public void Split_UsesCapturedOffsetAndSourceDates()
        {
            var t = Template(); var start = new DateTimeOffset(2026, 3, 1, 23, 59, 59, TimeSpan.FromHours(9));
            var split = _service.Split(start, 1000, 2000, t);
            Assert.That(split.Select(x => x.SourceLocalDate), Is.EqualTo(new[] { "2026-03-01", "2026-03-02" }));
            Assert.That(split.Select(x => x.DurationMs), Is.EqualTo(new[] { 1000L, 1000L }));
            Assert.That(split[0].StartedAtUtcMs, Is.EqualTo(1000)); Assert.That(split[1].StartedAtUtcMs, Is.EqualTo(2000));
        }

        [Test]
        public void Split_RejectsBlankDurableBaseEventId()
        {
            var template = Template(); template.SettlementEventId = " ";
            Assert.That(() => _service.Split(DateTimeOffset.Now, 1, 1, template), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task Outbox_CurrentRoundTripLoadsSettlementEvents()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "outbox.json"); var outbox = new PlaybackStatisticsOutbox(path);
            try { await outbox.SaveSettlementEventsAsync(new[] { Template() }); var state = outbox.LoadState(); Assert.That(state.Version, Is.EqualTo(2)); Assert.That(state.SettlementEvents.Single().SettlementEventId, Is.EqualTo("event")); }
            finally { Directory.Delete(Path.GetDirectoryName(path), true); }
        }

        [Test]
        public async Task Apply_IsIdempotentAndLinksExactSong()
        {
            var track = Track("song.mp3", 1000); var context = _service.GetRecordingContext(); var settlement = Template(); settlement.LocalTrackId = track; settlement.DeviceId = context.DeviceId; settlement.Generation = context.CurrentGeneration;
            Assert.That(await _service.ApplyAsync(settlement), Is.True); Assert.That(await _service.ApplyAsync(settlement), Is.False);
            var state = _repo.LoadState(); Assert.That(state.Songs.Single().LocalTrackId, Is.EqualTo(track)); Assert.That(state.Contributions.Single().DailyBuckets.Single().ListenMs, Is.EqualTo(1000));
        }

        [Test]
        public async Task Apply_DurationChangeCreatesNewExactWireSongWithoutMovingOldStatistics()
        {
            var track = Track("duration-change.mp3", 1000); var context = _service.GetRecordingContext();
            var old = Template(); SetIdentity(old, "duration-change.mp3", 1000); old.LocalTrackId = track; old.DeviceId = context.DeviceId; old.Generation = context.CurrentGeneration; old.DurationMs = 11; old.SettlementEventId = "duration-old";
            Assert.That(await _service.ApplyAsync(old), Is.True);

            using (var db = _db.GetConnection()) db.Execute("UPDATE Tracks SET DurationMs=1100 WHERE Id=@track", new { track });
            var changed = Template(); SetIdentity(changed, "duration-change.mp3", 1100); changed.LocalTrackId = track; changed.DeviceId = context.DeviceId; changed.Generation = context.CurrentGeneration; changed.DurationMs = 23; changed.SettlementEventId = "duration-new";
            Assert.That(await _service.ApplyAsync(changed), Is.True);

            var state = _repo.LoadState();
            Assert.That(state.Songs, Has.Count.EqualTo(2));
            Assert.That(state.Songs.All(x => x.LocalTrackId == track), Is.True);
            Assert.That(state.Contributions.Single(x => x.SongId == state.Songs.Single(s => s.DurationMs == 1000).SongId).DailyBuckets.Single().ListenMs, Is.EqualTo(11));
            Assert.That(state.Contributions.Single(x => x.SongId == state.Songs.Single(s => s.DurationMs == 1100).SongId).DailyBuckets.Single().ListenMs, Is.EqualTo(23));
        }

        [Test]
        public async Task Ranking_ExcludesUnlinkedAndTombstonedAndIncludesUndatedOnlyAll()
        {
            var linked = Track("linked.mp3", 1000); var c = _service.GetRecordingContext(); var dated = Template(); SetIdentity(dated, "linked.mp3", 1000); dated.LocalTrackId = linked; dated.DeviceId = c.DeviceId; dated.Generation = c.CurrentGeneration; dated.SourceLocalDate = "2026-03-01"; await _service.ApplyAsync(dated);
            var undated = Template(); SetIdentity(undated, "linked.mp3", 1000); undated.SettlementEventId = "undated"; undated.LocalTrackId = linked; undated.DeviceId = c.DeviceId; undated.Generation = c.CurrentGeneration; undated.SourceLocalDate = null; await _service.ApplyAsync(undated);
            Assert.That((await _service.GetTopTracksAsync(PlaybackStatisticsPeriod.Day, 5, new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero))).Single().TotalDurationMs, Is.EqualTo(1000));
            Assert.That((await _service.GetTopTracksAsync(PlaybackStatisticsPeriod.All)).Single().TotalDurationMs, Is.EqualTo(2000));
            _repo.InsertTombstone(new PlaybackSyncTombstone { DeviceId = c.DeviceId, Generation = c.CurrentGeneration, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = c.DeviceId, Reason = "test" });
            Assert.That(await _service.GetTopTracksAsync(PlaybackStatisticsPeriod.All), Is.Empty);
        }

        [Test]
        public async Task Ranking_FiltersUnresolvableSongsBeforeApplyingLimit()
        {
            var context = _service.GetRecordingContext();
            var unmatched = Template(); unmatched.SettlementEventId = "unmatched"; unmatched.FileName = "unmatched.mp3"; unmatched.NormalizedFileName = SyncSnapshotSerializer.NormalizePlaybackSongFileName(unmatched.FileName); unmatched.DeviceId = context.DeviceId; unmatched.Generation = context.CurrentGeneration; unmatched.LocalTrackId = null; unmatched.DurationMs = 2000;
            var track = Track("ranked.mp3", 1000); var linked = Template(); linked.SettlementEventId = "linked"; linked.FileName = "ranked.mp3"; linked.NormalizedFileName = SyncSnapshotSerializer.NormalizePlaybackSongFileName(linked.FileName); linked.DeviceId = context.DeviceId; linked.Generation = context.CurrentGeneration; linked.LocalTrackId = track;
            await _service.ApplyAsync(unmatched); await _service.ApplyAsync(linked);
            Assert.That((await _service.GetTopTracksAsync(PlaybackStatisticsPeriod.All, 1)).Single().TrackId, Is.EqualTo(track));
        }

        [Test]
        public async Task Ranking_AggregatesByLocalTrackBeforeLimitAndSaturatesWithTrackTieBreak()
        {
            var aggregateTrack = Track("aggregate.mp3", 1000); var tieTrack = Track("tie.mp3", 1000); var context = _service.GetRecordingContext();
            var first = _repo.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "aggregate.mp3", DurationMs = 1000 }, aggregateTrack);
            var second = _repo.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "aggregate.mp3", DurationMs = 1001 }, aggregateTrack);
            var tie = _repo.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "tie.mp3", DurationMs = 1000 }, tieTrack);
            _repo.MergeContribution(new PlaybackSyncContribution { SongId = first.SongId, DeviceId = context.DeviceId, Generation = context.CurrentGeneration, UndatedListenMs = long.MaxValue - 1, UpdatedAtUtcMs = 1, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-01", ListenMs = 10 } } });
            _repo.MergeContribution(new PlaybackSyncContribution { SongId = second.SongId, DeviceId = context.DeviceId, Generation = context.CurrentGeneration, UndatedListenMs = 10, UpdatedAtUtcMs = 1 });
            _repo.MergeContribution(new PlaybackSyncContribution { SongId = tie.SongId, DeviceId = context.DeviceId, Generation = context.CurrentGeneration, UndatedListenMs = long.MaxValue, UpdatedAtUtcMs = 1 });

            var top = await _service.GetTopTracksAsync(PlaybackStatisticsPeriod.All, 1);
            Assert.That(top.Single().TrackId, Is.EqualTo(aggregateTrack));
            Assert.That(top.Single().TotalDurationMs, Is.EqualTo(long.MaxValue));
            Assert.That((await _service.GetTopTracksAsync(PlaybackStatisticsPeriod.All, 2)).Select(x => x.TrackId), Is.EqualTo(new[] { aggregateTrack, tieTrack }));
        }

        [Test]
        public void Context_ReusesStableDeviceAndCurrentGeneration()
        {
            var first = _service.GetRecordingContext(); var firstDevice = _repo.LoadState().Devices.Single(x => x.DeviceId == first.DeviceId); _repo.AdvanceLocalDeviceGeneration(first.DeviceId, 3, 2); var second = _service.GetRecordingContext(); var secondDevice = _repo.LoadState().Devices.Single(x => x.DeviceId == second.DeviceId);
            Assert.That(second.DeviceId, Is.EqualTo(first.DeviceId)); Assert.That(second.CurrentGeneration, Is.EqualTo(3));
            Assert.That(firstDevice.DisplayName, Is.Not.Null.And.Not.Empty); Assert.That(firstDevice.DisplayNameUpdatedAtUtcMs, Is.GreaterThan(0));
            Assert.That(secondDevice.DisplayName, Is.EqualTo(firstDevice.DisplayName)); Assert.That(secondDevice.DisplayNameUpdatedAtUtcMs, Is.EqualTo(firstDevice.DisplayNameUpdatedAtUtcMs));
        }

        [Test]
        public void Context_RepairsBlankCurrentLocalDevice()
        {
            _db.SetSetting("Sync.DeviceId", "local-device");
            _repo.EnsureDevice(new PlaybackSyncDevice { DeviceId = "local-device", CurrentGeneration = 2, DisplayName = " ", DisplayNameUpdatedAtUtcMs = 0, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });

            var context = _service.GetRecordingContext();
            var device = _repo.LoadState().Devices.Single(x => x.DeviceId == context.DeviceId);

            Assert.That(device.DisplayName, Is.Not.Null.And.Not.Empty);
            Assert.That(device.DisplayName.Trim(), Is.Not.Empty);
            Assert.That(device.DisplayNameUpdatedAtUtcMs, Is.GreaterThan(0));
            Assert.That(device.CurrentGeneration, Is.EqualTo(2));
        }

        [Test]
        public void Context_PreservesValidRenamedLocalDevice()
        {
            _db.SetSetting("Sync.DeviceId", "local-device");
            _repo.EnsureDevice(new PlaybackSyncDevice { DeviceId = "local-device", CurrentGeneration = 0, DisplayName = "Renamed desktop", DisplayNameUpdatedAtUtcMs = 10, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });

            _service.GetRecordingContext();
            var device = _repo.LoadState().Devices.Single(x => x.DeviceId == "local-device");

            Assert.That(device.DisplayName, Is.EqualTo("Renamed desktop"));
            Assert.That(device.DisplayNameUpdatedAtUtcMs, Is.EqualTo(10));
        }

        [Test]
        public async Task Apply_RepairsCurrentLocalDeviceAndRejectsOtherDevice()
        {
            _db.SetSetting("Sync.DeviceId", "local-device");
            _repo.EnsureDevice(new PlaybackSyncDevice { DeviceId = "local-device", CurrentGeneration = 0, DisplayName = null, DisplayNameUpdatedAtUtcMs = 0, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });
            var settlement = Template(); settlement.DeviceId = "remote-device";

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.ApplyAsync(settlement));
            var device = _repo.LoadState().Devices.Single(x => x.DeviceId == "local-device");
            Assert.That(device.DisplayName, Is.Not.Null.And.Not.Empty);
            Assert.That(device.DisplayNameUpdatedAtUtcMs, Is.GreaterThan(0));
            Assert.That(_repo.LoadState().Devices.Any(x => x.DeviceId == "remote-device"), Is.False);
        }

        [Test]
        public async Task ClearCurrentGeneration_TombstonesDeletesAndRotatesAboveKnownGenerations()
        {
            var track = Track("clear.mp3", 1000); var context = _service.GetRecordingContext();
            var current = Template(); SetIdentity(current, "clear.mp3", 1000); current.LocalTrackId = track; current.DeviceId = context.DeviceId; current.Generation = context.CurrentGeneration;
            await _service.ApplyAsync(current);
            _repo.InsertTombstone(new PlaybackSyncTombstone { DeviceId = context.DeviceId, Generation = 5, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = context.DeviceId, Reason = "known" });
            _repo.EnsureDevice(new PlaybackSyncDevice { DeviceId = "other", CurrentGeneration = 9, DisplayName = "Other", DisplayNameUpdatedAtUtcMs = 1, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });
            var result = await _service.ClearCurrentGenerationAsync();
            Assert.That(result.OldGeneration, Is.EqualTo(context.CurrentGeneration)); Assert.That(result.NewGeneration, Is.EqualTo(6));
            var state = _repo.LoadState(); Assert.That(state.Tombstones.Any(x => x.DeviceId == context.DeviceId && x.Generation == context.CurrentGeneration && x.Reason == "localClear"), Is.True);
            Assert.That(state.Contributions.Any(x => x.DeviceId == context.DeviceId && x.Generation == context.CurrentGeneration), Is.False);
            var future = Template(); SetIdentity(future, "clear.mp3", 1000); future.SettlementEventId = "future"; future.LocalTrackId = track; future.DeviceId = context.DeviceId; future.Generation = _service.GetRecordingContext().CurrentGeneration;
            await _service.ApplyAsync(future); Assert.That(_repo.LoadState().Contributions.Single(x => x.DeviceId == context.DeviceId).Generation, Is.EqualTo(result.NewGeneration));
        }

        [Test]
        public async Task ObserveCurrentGenerationTombstone_RotatesOnceOnlyForExactCurrentTombstone()
        {
            var track = Track("observed.mp3", 1000); var context = _service.GetRecordingContext();
            var settlement = Template(); SetIdentity(settlement, "observed.mp3", 1000); settlement.LocalTrackId = track; settlement.DeviceId = context.DeviceId; settlement.Generation = context.CurrentGeneration;
            await _service.ApplyAsync(settlement);
            _repo.InsertTombstone(new PlaybackSyncTombstone { DeviceId = context.DeviceId, Generation = context.CurrentGeneration, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = context.DeviceId, Reason = "remote" });
            _repo.InsertTombstone(new PlaybackSyncTombstone { DeviceId = context.DeviceId, Generation = 7, Scope = "deviceGeneration", TombstonedAtUtcMs = 1, TombstonedByDeviceId = context.DeviceId, Reason = "known" });
            var rotated = await _service.ObserveCurrentGenerationTombstoneAsync(); var repeated = await _service.ObserveCurrentGenerationTombstoneAsync();
            Assert.That(rotated.Rotated, Is.True); Assert.That(rotated.OldGeneration, Is.EqualTo(context.CurrentGeneration)); Assert.That(rotated.NewGeneration, Is.EqualTo(8)); Assert.That(rotated.AffectedContributionCount, Is.EqualTo(1));
            Assert.That(repeated.Rotated, Is.False); Assert.That(_service.GetRecordingContext().CurrentGeneration, Is.EqualTo(8));
            Assert.That(_repo.LoadState().Contributions.Any(x => x.DeviceId == context.DeviceId && x.Generation == context.CurrentGeneration), Is.False);
        }

        [Test]
        public async Task Apply_NewExactSongSharesTrackAndPreservesOldStatistics()
        {
            var track = Track("live.mp3", 1000); var context = _service.GetRecordingContext();
            var old = _repo.EnsureSong(new PlaybackSyncSong { FileName = "live.mp3", DurationMs = 900 }); Bind(old.SongId, track);
            _repo.MergeContribution(new PlaybackSyncContribution { SongId = old.SongId, DeviceId = context.DeviceId, Generation = context.CurrentGeneration, UndatedListenMs = 7, FirstPlayedAtUtcMs = 10, LastPlayedAtUtcMs = 20, UpdatedAtUtcMs = 20, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-01", ListenMs = 11 } } });
            _repo.RecordSettlement(new PlaybackSyncSettlement { SettlementEventId = "old-live", SongId = old.SongId, DeviceId = context.DeviceId, Generation = context.CurrentGeneration, AppliedAtUtcMs = 1, SourceKind = "live" }, 0, null, null, null);

            var incoming = Template(); SetIdentity(incoming, "live.mp3", 1000); incoming.SettlementEventId = "new-live"; incoming.LocalTrackId = track; incoming.DeviceId = context.DeviceId; incoming.Generation = context.CurrentGeneration;
            Assert.That(await _service.ApplyAsync(incoming), Is.True);

            var state = _repo.LoadState(); var exact = state.Songs.Single(x => x.DurationMs == 1000); var preserved = state.Contributions.Single(x => x.SongId == old.SongId);
            Assert.That(exact.LocalTrackId, Is.EqualTo(track)); Assert.That(state.Songs, Has.Count.EqualTo(2)); Assert.That(state.Songs.Single(x => x.SongId == old.SongId).LocalTrackId, Is.EqualTo(track));
            Assert.That(preserved.UndatedListenMs, Is.EqualTo(7)); Assert.That(preserved.FirstPlayedAtUtcMs, Is.EqualTo(10)); Assert.That(preserved.LastPlayedAtUtcMs, Is.EqualTo(20)); Assert.That(preserved.UpdatedAtUtcMs, Is.EqualTo(20)); Assert.That(preserved.DailyBuckets.Single().ListenMs, Is.EqualTo(11));
            using (var db = _db.GetConnection())
            {
                var settlement = db.QuerySingle<dynamic>("SELECT SongId,AppliedAtUtcMs,SourceKind FROM PlaybackStatisticsSettlements WHERE SettlementEventId='old-live'");
                Assert.That((long)settlement.SongId, Is.EqualTo(old.SongId)); Assert.That((long)settlement.AppliedAtUtcMs, Is.EqualTo(1)); Assert.That((string)settlement.SourceKind, Is.EqualTo("live"));
            }
        }

        [Test]
        public async Task Apply_ExistingExactSongSharesTrackWithoutMovingOldBinding()
        {
            var track = Track("existing.mp3", 1000); var context = _service.GetRecordingContext();
            var old = _repo.EnsureSong(new PlaybackSyncSong { FileName = "existing.mp3", DurationMs = 900 }); Bind(old.SongId, track);
            var exact = _repo.EnsureSong(new PlaybackSyncSong { FileName = "existing.mp3", DurationMs = 1000 });
            var incoming = Template(); SetIdentity(incoming, "existing.mp3", 1000); incoming.SettlementEventId = "existing-exact"; incoming.LocalTrackId = track; incoming.DeviceId = context.DeviceId; incoming.Generation = context.CurrentGeneration;

            Assert.That(await _service.ApplyAsync(incoming), Is.True);

            var state = _repo.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == exact.SongId).LocalTrackId, Is.EqualTo(track));
            Assert.That(state.Songs.Single(x => x.SongId == old.SongId).LocalTrackId, Is.EqualTo(track));
            Assert.That(state.Songs.Count(x => x.LocalTrackId == track), Is.EqualTo(2));
        }

        [Test]
        public void PendingFilter_RemovesOnlyTheRotatedDeviceGeneration()
        {
            var values = new[] { Template(), new PlaybackStatisticsSettlement { SettlementEventId = "same-device-new", DeviceId = "device", Generation = 1 }, new PlaybackStatisticsSettlement { SettlementEventId = "other-device", DeviceId = "other", Generation = 0 } };
            Assert.That(PlaybackStatisticsSettlementFilter.ExcludingGeneration(values, "device", 0).Select(x => x.SettlementEventId), Is.EquivalentTo(new[] { "same-device-new", "other-device" }));
        }

        [Test]
        public void RelinkSongs_ExactAndUniqueFuzzyLinkWithoutStealingOrAmbiguousMatches()
        {
            var exactTrack = Track("exact.mp3", 1000); var fuzzyTrack = Track("fuzzy.mp3", 1100); var ambiguousA = Track("ambiguous.mp3", 2000, 2); var ambiguousB = Track("ambiguous.mp3", 2100, 3); var manyTrack = Track("many.mp3", 2400, 4); var linkedTrack = Track("linked.mp3", 1000);
            var exact = _repo.EnsureSong(new PlaybackSyncSong { FileName = "exact.mp3", DurationMs = 1000 });
            var fuzzy = _repo.EnsureSong(new PlaybackSyncSong { FileName = "fuzzy.mp3", DurationMs = 1001 });
            var ambiguousSong = _repo.EnsureSong(new PlaybackSyncSong { FileName = "ambiguous.mp3", DurationMs = 2050 });
            var manyA = _repo.EnsureSong(new PlaybackSyncSong { FileName = "many.mp3", DurationMs = 2300 }); var manyB = _repo.EnsureSong(new PlaybackSyncSong { FileName = "many.mp3", DurationMs = 2350 });
            var linked = _repo.EnsureSong(new PlaybackSyncSong { FileName = "linked.mp3", DurationMs = 1000, LocalTrackId = linkedTrack });
            Assert.That(_repo.RelinkSongs(), Is.EqualTo(4));
            var state = _repo.LoadState(); Assert.That(state.Songs.Single(x => x.SongId == exact.SongId).LocalTrackId, Is.EqualTo(exactTrack)); Assert.That(state.Songs.Single(x => x.SongId == fuzzy.SongId).LocalTrackId, Is.EqualTo(fuzzyTrack));
            Assert.That(state.Songs.Single(x => x.SongId == ambiguousSong.SongId).LocalTrackId, Is.Null); Assert.That(state.Songs.Single(x => x.SongId == linked.SongId).LocalTrackId, Is.EqualTo(linkedTrack));
            Assert.That(state.Songs.Single(x => x.SongId == manyA.SongId).LocalTrackId, Is.EqualTo(manyTrack)); Assert.That(state.Songs.Single(x => x.SongId == manyB.SongId).LocalTrackId, Is.EqualTo(manyTrack));
            Assert.That(ambiguousA, Is.Not.EqualTo(ambiguousB));
        }

        [Test]
        public async Task SourceDevices_RenameAndTombstoneRespectLocalAndEffectiveTotals()
        {
            var track = Track("source.mp3", 1000); var local = _service.GetRecordingContext();
            _repo.EnsureDevice(new PlaybackSyncDevice { DeviceId = "remote", CurrentGeneration = 2, DisplayName = "Remote", DisplayNameUpdatedAtUtcMs = 1, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });
            var song = _repo.EnsureSong(new PlaybackSyncSong { FileName = "source.mp3", DurationMs = 1000, LocalTrackId = track });
            _repo.MergeContribution(new PlaybackSyncContribution { SongId = song.SongId, DeviceId = "remote", Generation = 2, UndatedListenMs = long.MaxValue - 1, UpdatedAtUtcMs = 1, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-01-01", ListenMs = 10 } } });
            await _service.RenameDeviceAsync("remote", "  Zed  ", 2); await _service.RenameDeviceAsync("remote", "Alpha", 2);
            var remote = (await _service.GetSourceDevicesAsync()).Single(x => x.DeviceId == "remote");
            Assert.That(remote.DisplayName, Is.EqualTo("Zed")); Assert.That(remote.EffectiveTotalListenMs, Is.EqualTo(long.MaxValue)); Assert.That(remote.KnownActiveGenerationCount, Is.EqualTo(1));
            Assert.That(await _service.TombstoneKnownActiveGenerationsAsync(new[] { local.DeviceId, "remote" }, 3, local.DeviceId, "privacy"), Is.EqualTo(1));
            Assert.That((await _service.GetSourceDevicesAsync()).Single(x => x.DeviceId == "remote").EffectiveTotalListenMs, Is.EqualTo(0));
            _repo.EnsureDevice(new PlaybackSyncDevice { DeviceId = "remote-two", CurrentGeneration = 1, DisplayName = "Remote two", DisplayNameUpdatedAtUtcMs = 1, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });
            Assert.That(await _service.TombstoneAllKnownNonLocalGenerationsAsync(4, local.DeviceId, "privacy"), Is.EqualTo(1));
            Assert.That(_service.GetRecordingContext().CurrentGeneration, Is.EqualTo(local.CurrentGeneration));
        }

        [TestCase(9, 9, false)]
        [TestCase(9, 8, true)]
        [TestCase(-5, 0, true)]
        public void OffsetHelper_DetectsPlaybackOffsetChanges(int capturedHours, int currentHours, bool expected)
        {
            var sourceLocalStart = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(capturedHours));
            Assert.That(PlaybackStatisticsOffsetHelper.HasOffsetChanged(sourceLocalStart, TimeSpan.FromHours(currentHours)), Is.EqualTo(expected));
        }

        private int Track(string name, long duration, long totalSamples = 1) { using (var c = _db.GetConnection()) return c.ExecuteScalar<int>("INSERT INTO Tracks(FileName,DurationMs,TotalSamples) VALUES (@name,@duration,@totalSamples); SELECT last_insert_rowid();", new { name, duration, totalSamples }); }
        private void Bind(long songId, int trackId) { using (var db = _db.GetConnection()) db.Execute("UPDATE PlaybackSyncSongs SET LocalTrackId=@trackId WHERE SongId=@songId", new { songId, trackId }); }
        private static void SetIdentity(PlaybackStatisticsSettlement settlement, string fileName, long duration) { settlement.FileName = fileName; settlement.NormalizedFileName = SyncSnapshotSerializer.NormalizePlaybackSongFileName(fileName); settlement.TrackDurationMs = duration; }
        private static PlaybackStatisticsSettlement Template() => new PlaybackStatisticsSettlement { SettlementEventId = "event", FileName = "song.mp3", NormalizedFileName = SyncSnapshotSerializer.NormalizePlaybackSongFileName("song.mp3"), TrackDurationMs = 1000, DeviceId = "device", Generation = 0, SourceLocalDate = "2026-03-01", StartedAtUtcMs = 1, DurationMs = 1000, AppliedAtUtcMs = 2, SourceKind = "live" };
    }
}
