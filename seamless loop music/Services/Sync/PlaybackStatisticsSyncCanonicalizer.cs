using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>Pure canonicalization and max/union merge for schema v2 playback statistics.</summary>
    public static class PlaybackStatisticsSyncCanonicalizer
    {
        public static SyncPlaybackStatistics Empty()
        {
            return new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice>(),
                Songs = new List<SyncPlaybackSong>(),
                Tombstones = new List<SyncPlaybackTombstone>()
            };
        }

        public static SyncPlaybackStatistics Canonicalize(SyncPlaybackStatistics statistics)
        {
            statistics ??= Empty();
            var result = new SyncPlaybackStatistics
            {
                DateBucketBasis = statistics.DateBucketBasis ?? SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = (statistics.Devices ?? new List<SyncPlaybackDevice>()).Select(Clone).OrderBy(x => x.DeviceId, StringComparer.Ordinal).ToList(),
                Songs = (statistics.Songs ?? new List<SyncPlaybackSong>()).Select(Clone).OrderBy(x => x.Song?.NormalizedFileName, StringComparer.Ordinal).ThenBy(x => x.Song?.DurationMs ?? 0).ToList(),
                Tombstones = (statistics.Tombstones ?? new List<SyncPlaybackTombstone>()).Select(Clone).OrderBy(x => x.DeviceId, StringComparer.Ordinal).ThenBy(x => x.Generation).ThenBy(x => x.Scope, StringComparer.Ordinal).ToList()
            };
            foreach (var song in result.Songs)
                song.Contributions = (song.Contributions ?? new List<SyncPlaybackContribution>()).OrderBy(x => x.DeviceId, StringComparer.Ordinal).ThenBy(x => x.Generation).ToList();
            var suppressed = new HashSet<string>(result.Tombstones.Select(TombstoneKey), StringComparer.Ordinal);
            foreach (var song in result.Songs)
                song.Contributions = song.Contributions.Where(x => !suppressed.Contains(ContributionTombstoneKey(x))).ToList();
            return result;
        }

        public static SyncPlaybackStatistics Merge(SyncPlaybackStatistics left, SyncPlaybackStatistics right)
        {
            left = Canonicalize(left);
            right = Canonicalize(right);
            var result = Empty();
            result.Devices = MergeDevices(left.Devices, right.Devices);
            result.Songs = MergeSongs(left.Songs, right.Songs);
            result.Tombstones = MergeTombstones(left.Tombstones, right.Tombstones);

            var suppressed = new HashSet<string>(result.Tombstones.Select(TombstoneKey), StringComparer.Ordinal);
            foreach (var song in result.Songs)
                song.Contributions = song.Contributions.Where(c => !suppressed.Contains(ContributionTombstoneKey(c))).ToList();
            return Canonicalize(result);
        }

        private static List<SyncPlaybackDevice> MergeDevices(IEnumerable<SyncPlaybackDevice> left, IEnumerable<SyncPlaybackDevice> right)
        {
            var all = left.Concat(right).GroupBy(x => x.DeviceId, StringComparer.Ordinal);
            var result = new List<SyncPlaybackDevice>();
            foreach (var group in all)
            {
                var devices = group.ToList();
                if (devices.Select(x => x.Platform).Distinct(StringComparer.Ordinal).Count() > 1)
                    throw new FormatException($"Conflicting platforms for playback device '{group.Key}'.");
                var displayWinner = devices.Aggregate((best, next) => CompareDisplayName(next, best) > 0 ? next : best);
                result.Add(new SyncPlaybackDevice
                {
                    DeviceId = group.Key,
                    CurrentGeneration = devices.Max(x => x.CurrentGeneration),
                    DisplayName = displayWinner.DisplayName,
                    DisplayNameUpdatedAtUtcMs = displayWinner.DisplayNameUpdatedAtUtcMs,
                    Platform = devices[0].Platform,
                    FirstSeenAtUtcMs = devices.Min(x => x.FirstSeenAtUtcMs),
                    LastSeenAtUtcMs = devices.Max(x => x.LastSeenAtUtcMs)
                });
            }
            return result;
        }

        private static int CompareDisplayName(SyncPlaybackDevice left, SyncPlaybackDevice right)
        {
            var timestamp = left.DisplayNameUpdatedAtUtcMs.CompareTo(right.DisplayNameUpdatedAtUtcMs);
            return timestamp != 0 ? timestamp : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
        }

        private static List<SyncPlaybackSong> MergeSongs(IEnumerable<SyncPlaybackSong> left, IEnumerable<SyncPlaybackSong> right)
        {
            var all = left.Concat(right).GroupBy(x => SongKey(x.Song), StringComparer.Ordinal);
            var result = new List<SyncPlaybackSong>();
            foreach (var group in all)
            {
                var songs = group.ToList();
                var identities = songs.Select(x => x.Song).ToList();
                var identity = new SyncPlaybackSongIdentity
                {
                    FileName = OrdinalMax(identities.Select(x => x.FileName)),
                    NormalizedFileName = identities[0].NormalizedFileName,
                    DurationMs = identities[0].DurationMs,
                    TotalSamples = MaxNullable(identities.Select(x => x.TotalSamples)),
                    ContentHash = OrdinalMax(identities.Select(x => x.ContentHash))
                };
                var contributions = songs.SelectMany(x => x.Contributions ?? new List<SyncPlaybackContribution>())
                    .GroupBy(ContributionKey, StringComparer.Ordinal)
                    .Select(grouped => MergeContribution(grouped)).ToList();
                result.Add(new SyncPlaybackSong { Song = identity, Contributions = contributions });
            }
            return result;
        }

        private static SyncPlaybackContribution MergeContribution(IGrouping<string, SyncPlaybackContribution> group)
        {
            var values = group.ToList();
            var daily = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in values.SelectMany(x => x.DatedListenMs ?? new Dictionary<string, long>()))
                daily[entry.Key] = daily.TryGetValue(entry.Key, out var existing) ? Math.Max(existing, entry.Value) : entry.Value;
            return new SyncPlaybackContribution
            {
                DeviceId = values[0].DeviceId,
                Generation = values[0].Generation,
                DatedListenMs = daily,
                UndatedListenMs = values.Max(x => x.UndatedListenMs),
                FirstPlayedAtUtcMs = MinNonZero(values.Select(x => x.FirstPlayedAtUtcMs)),
                LastPlayedAtUtcMs = MaxNullable(values.Select(x => x.LastPlayedAtUtcMs)),
                UpdatedAtUtcMs = values.Max(x => x.UpdatedAtUtcMs)
            };
        }

        private static List<SyncPlaybackTombstone> MergeTombstones(IEnumerable<SyncPlaybackTombstone> left, IEnumerable<SyncPlaybackTombstone> right)
        {
            return left.Concat(right).GroupBy(TombstoneKey, StringComparer.Ordinal).Select(group =>
                group.Aggregate((best, next) => CompareTombstone(next, best) > 0 ? next : best)).Select(Clone).ToList();
        }

        private static int CompareTombstone(SyncPlaybackTombstone left, SyncPlaybackTombstone right)
        {
            var timestamp = left.TombstonedAtUtcMs.CompareTo(right.TombstonedAtUtcMs);
            if (timestamp != 0) return timestamp;
            var actor = string.Compare(left.TombstonedByDeviceId, right.TombstonedByDeviceId, StringComparison.Ordinal);
            return actor != 0 ? actor : string.Compare(left.Reason, right.Reason, StringComparison.Ordinal);
        }

        private static string SongKey(SyncPlaybackSongIdentity song) => song.NormalizedFileName + "\u001f" + song.DurationMs.ToString(CultureInfo.InvariantCulture);
        private static string ContributionKey(SyncPlaybackContribution contribution) => contribution.DeviceId + "\u001f" + contribution.Generation.ToString(CultureInfo.InvariantCulture);
        private static string TombstoneKey(SyncPlaybackTombstone tombstone) => tombstone.DeviceId + "\u001f" + tombstone.Generation.ToString(CultureInfo.InvariantCulture) + "\u001f" + tombstone.Scope;
        private static string ContributionTombstoneKey(SyncPlaybackContribution contribution) => contribution.DeviceId + "\u001f" + contribution.Generation.ToString(CultureInfo.InvariantCulture) + "\u001f" + SyncSnapshotSerializer.DeviceGenerationTombstoneScope;
        private static string OrdinalMax(IEnumerable<string> values) => values.Where(x => x != null).OrderBy(x => x, StringComparer.Ordinal).LastOrDefault();
        private static long? MaxNullable(IEnumerable<long?> values)
        {
            var present = values.Where(x => x.HasValue).Select(x => x.Value).ToList();
            return present.Count == 0 ? (long?)null : present.Max();
        }
        private static long? MinNonZero(IEnumerable<long?> values)
        {
            var nonZero = values.Where(x => x.GetValueOrDefault() > 0).Select(x => x.Value).ToList();
            return nonZero.Count == 0 ? (long?)null : nonZero.Min();
        }

        private static SyncPlaybackDevice Clone(SyncPlaybackDevice value) => new SyncPlaybackDevice { DeviceId = value.DeviceId, CurrentGeneration = value.CurrentGeneration, DisplayName = value.DisplayName, DisplayNameUpdatedAtUtcMs = value.DisplayNameUpdatedAtUtcMs, Platform = value.Platform, FirstSeenAtUtcMs = value.FirstSeenAtUtcMs, LastSeenAtUtcMs = value.LastSeenAtUtcMs };
        private static SyncPlaybackSong Clone(SyncPlaybackSong value) => new SyncPlaybackSong { Song = Clone(value.Song), Contributions = (value.Contributions ?? new List<SyncPlaybackContribution>()).Select(Clone).ToList() };
        private static SyncPlaybackSongIdentity Clone(SyncPlaybackSongIdentity value) => new SyncPlaybackSongIdentity { FileName = value.FileName, NormalizedFileName = value.NormalizedFileName, DurationMs = value.DurationMs, TotalSamples = value.TotalSamples, ContentHash = value.ContentHash };
        private static SyncPlaybackContribution Clone(SyncPlaybackContribution value) => new SyncPlaybackContribution { DeviceId = value.DeviceId, Generation = value.Generation, DatedListenMs = new Dictionary<string, long>(value.DatedListenMs ?? new Dictionary<string, long>(), StringComparer.Ordinal), UndatedListenMs = value.UndatedListenMs, FirstPlayedAtUtcMs = value.FirstPlayedAtUtcMs, LastPlayedAtUtcMs = value.LastPlayedAtUtcMs, UpdatedAtUtcMs = value.UpdatedAtUtcMs };
        private static SyncPlaybackTombstone Clone(SyncPlaybackTombstone value) => new SyncPlaybackTombstone { DeviceId = value.DeviceId, Generation = value.Generation, Scope = value.Scope, TombstonedAtUtcMs = value.TombstonedAtUtcMs, TombstonedByDeviceId = value.TombstonedByDeviceId, Reason = value.Reason };
    }

    public sealed class SyncDatedListenMsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Dictionary<string, long>);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            foreach (var pair in ((Dictionary<string, long>)value).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteValue(pair.Value);
            }
            writer.WriteEndObject();
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => serializer.Deserialize<Dictionary<string, long>>(reader);
    }
}
