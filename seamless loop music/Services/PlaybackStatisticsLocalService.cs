using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Events;
using Prism.Events;

namespace seamless_loop_music.Services
{
    public sealed class PlaybackStatisticsLocalService : IPlaybackStatisticsLocalService
    {
        private readonly IDatabaseHelper _database;
        private readonly IPlaybackStatisticsSyncRepository _sync;

        public PlaybackStatisticsLocalService(IDatabaseHelper database, IPlaybackStatisticsSyncRepository sync) : this(database, sync, null) { }
        public PlaybackStatisticsLocalService(IDatabaseHelper database, IPlaybackStatisticsSyncRepository sync, IEventAggregator eventAggregator)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database)); _sync = sync ?? throw new ArgumentNullException(nameof(sync));
            if (eventAggregator != null) eventAggregator.GetEvent<LibraryRefreshedEvent>().Subscribe(OnLibraryRefreshed, ThreadOption.BackgroundThread, false);
        }

        public PlaybackStatisticsRecordingContext GetRecordingContext()
        {
            var id = _database.GetSetting(PlaybackStatisticsDeviceIdentity.DeviceKey);
            if (string.IsNullOrWhiteSpace(id)) { id = Guid.NewGuid().ToString("D"); _database.SetSetting(PlaybackStatisticsDeviceIdentity.DeviceKey, id); }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var device = _sync.EnsureLocalDevice(id, PlaybackStatisticsDeviceIdentity.CurrentWindowsDisplayName(), now);
            return new PlaybackStatisticsRecordingContext { DeviceId = id, CurrentGeneration = device.CurrentGeneration };
        }

        public IReadOnlyList<PlaybackStatisticsSettlement> Split(DateTimeOffset sourceLocalStart, long startedAtUtcMs, long durationMs, PlaybackStatisticsSettlement template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.SettlementEventId) || durationMs <= 0 || startedAtUtcMs < 0 || durationMs > long.MaxValue - startedAtUtcMs) throw new ArgumentOutOfRangeException(nameof(durationMs));
            var result = new List<PlaybackStatisticsSettlement>(); var local = sourceLocalStart; long remaining = durationMs; long utc = startedAtUtcMs;
            while (remaining > 0)
            {
                var next = local.Date.AddDays(1); var untilMidnight = (long)(next - local.DateTime).TotalMilliseconds;
                var take = Math.Min(remaining, untilMidnight);
                var item = Copy(template); item.SettlementEventId = template.SettlementEventId + ":" + result.Count;
                item.SourceLocalDate = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); item.StartedAtUtcMs = utc; item.DurationMs = take; result.Add(item);
                remaining -= take; utc += take; local = local.AddMilliseconds(take);
            }
            return result;
        }

        public Task<bool> ApplyAsync(PlaybackStatisticsSettlement settlement)
        {
            settlement.Validate();
            var context = GetRecordingContext();
            if (!string.Equals(settlement.DeviceId, context.DeviceId, StringComparison.Ordinal))
                throw new InvalidOperationException("Playback settlement deviceId must match the current local Sync.DeviceId.");
            var song = settlement.LocalTrackId.HasValue
                ? _sync.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = settlement.FileName, NormalizedFileName = settlement.NormalizedFileName, DurationMs = settlement.TrackDurationMs, TotalSamples = settlement.TotalSamples, ContentHash = settlement.ContentHash }, settlement.LocalTrackId.Value)
                : _sync.EnsureSong(new PlaybackSyncSong { FileName = settlement.FileName, NormalizedFileName = settlement.NormalizedFileName, DurationMs = settlement.TrackDurationMs, TotalSamples = settlement.TotalSamples, ContentHash = settlement.ContentHash });
            return Task.FromResult(_sync.RecordSettlement(new PlaybackSyncSettlement { SettlementEventId = settlement.SettlementEventId, SongId = song.SongId, DeviceId = settlement.DeviceId, Generation = settlement.Generation, AppliedAtUtcMs = settlement.AppliedAtUtcMs, SourceKind = settlement.SourceKind }, settlement.DurationMs, settlement.SourceLocalDate, settlement.StartedAtUtcMs, settlement.StartedAtUtcMs + settlement.DurationMs));
        }

        public Task<PlaybackStatisticsGenerationClearResult> ClearCurrentGenerationAsync()
        {
            var context = GetRecordingContext();
            return Task.FromResult(_sync.TombstoneAndRotateLocalGeneration(context.DeviceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        public Task<PlaybackStatisticsTombstoneObservationResult> ObserveCurrentGenerationTombstoneAsync()
        {
            var context = GetRecordingContext();
            return Task.FromResult(_sync.ObserveCurrentGenerationTombstone(context.DeviceId, context.CurrentGeneration, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        public Task<int> RelinkSongsAsync() { return Task.FromResult(_sync.RelinkSongs()); }
        public Task<IReadOnlyList<PlaybackStatisticsSourceDevice>> GetSourceDevicesAsync() { return Task.FromResult(_sync.GetSourceDevices(GetRecordingContext().DeviceId)); }
        public Task RenameDeviceAsync(string deviceId, string displayName, long updatedAtUtcMs) { _sync.RenameDevice(deviceId, displayName, updatedAtUtcMs); return Task.CompletedTask; }
        public Task<int> TombstoneKnownActiveGenerationsAsync(IEnumerable<string> deviceIds, long tombstonedAtUtcMs, string actorDeviceId, string reason) { return Task.FromResult(_sync.TombstoneKnownActiveGenerations(deviceIds, tombstonedAtUtcMs, actorDeviceId, reason, GetRecordingContext().DeviceId)); }
        public Task<int> TombstoneAllKnownNonLocalGenerationsAsync(long tombstonedAtUtcMs, string actorDeviceId, string reason) { return Task.FromResult(_sync.TombstoneAllKnownNonLocalGenerations(tombstonedAtUtcMs, actorDeviceId, reason, GetRecordingContext().DeviceId)); }

        public Task<List<PlaybackStatisticItem>> GetTopTracksAsync(PlaybackStatisticsPeriod period, int limit = 5, DateTimeOffset? viewerLocalNow = null)
        {
            var now = viewerLocalNow ?? DateTimeOffset.Now; var start = PeriodStart(period, now);
            var state = _sync.LoadState(); var dead = new HashSet<string>(state.Tombstones.Select(x => x.DeviceId + "|" + x.Generation));
            var linkedTracks = state.Songs.Where(x => x.LocalTrackId.HasValue).ToDictionary(x => x.SongId, x => x.LocalTrackId.Value);
            var today = now.ToString("yyyy-MM-dd");
            var totals = state.Contributions
                .Where(c => linkedTracks.ContainsKey(c.SongId) && !dead.Contains(c.DeviceId + "|" + c.Generation))
                .Select(c => new
                {
                    TrackId = linkedTracks[c.SongId],
                    Total = period == PlaybackStatisticsPeriod.All
                        ? c.DailyBuckets.Aggregate(c.UndatedListenMs, (v, x) => Saturating(v, x.ListenMs))
                        : c.DailyBuckets.Where(x => string.CompareOrdinal(x.LocalDate, start) >= 0 && string.CompareOrdinal(x.LocalDate, today) <= 0)
                            .Aggregate(0L, (v, x) => Saturating(v, x.ListenMs))
                })
                .Where(x => x.Total > 0)
                .GroupBy(x => x.TrackId)
                .Select(x => new { TrackId = x.Key, Total = x.Aggregate(0L, (v, y) => Saturating(v, y.Total)) })
                .OrderByDescending(x => x.Total)
                .ThenBy(x => x.TrackId)
                .Take(limit)
                .ToList();
            var values = new List<PlaybackStatisticItem>(); using (var db = _database.GetConnection()) foreach (var total in totals) { var item = db.QueryFirstOrDefault<PlaybackStatisticItem>("SELECT t.Id TrackId, COALESCE(NULLIF(t.DisplayName,''),t.FileName) Title, COALESCE(ar.Name,'') Artist, COALESCE(al.Name,'') Album, t.CoverPath TrackCoverPath, al.CoverPath AlbumCoverPath, ar.CoverPath ArtistCoverPath FROM Tracks t LEFT JOIN Artists ar ON ar.Id=t.ArtistId LEFT JOIN Albums al ON al.Id=t.AlbumId WHERE t.Id=@id", new { id = total.TrackId }); if (item != null) { item.TotalDurationMs = total.Total; values.Add(item); } }
            return Task.FromResult(values);
        }

        private void OnLibraryRefreshed()
        {
            try { _sync.RelinkSongs(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Playback statistics] Relink failed: {ex.Message}"); }
        }
        private static PlaybackStatisticsSettlement Copy(PlaybackStatisticsSettlement x) => new PlaybackStatisticsSettlement { FileName = x.FileName, NormalizedFileName = x.NormalizedFileName, TrackDurationMs = x.TrackDurationMs, TotalSamples = x.TotalSamples, ContentHash = x.ContentHash, LocalTrackId = x.LocalTrackId, DeviceId = x.DeviceId, Generation = x.Generation, AppliedAtUtcMs = x.AppliedAtUtcMs, SourceKind = x.SourceKind };
        private static long Saturating(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
        private static string PeriodStart(PlaybackStatisticsPeriod p, DateTimeOffset now) { var d = now.Date; switch (p) { case PlaybackStatisticsPeriod.Day: break; case PlaybackStatisticsPeriod.Week: d = d.AddDays(-((7 + ((int)d.DayOfWeek - 1) % 7) % 7)); break; case PlaybackStatisticsPeriod.Month: d = new DateTime(d.Year, d.Month, 1); break; case PlaybackStatisticsPeriod.Year: d = new DateTime(d.Year, 1, 1); break; case PlaybackStatisticsPeriod.All: return null; default: throw new ArgumentOutOfRangeException(nameof(p)); } return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }
}
