using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// JSON serializer for the cloud SyncSnapshot schema v2.
    /// Produces indented JSON; deserialization validates invariants.
    /// </summary>
    public static class SyncSnapshotSerializer
    {
        public const string SourceLocalDateBucketBasis = "sourceLocal";
        public const string DeviceGenerationTombstoneScope = "deviceGeneration";
        private static readonly JsonSerializerSettings SerializeSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            },
            NullValueHandling = NullValueHandling.Include,
            Converters = new List<JsonConverter> { new SyncDatedListenMsConverter() }
        };

        private static readonly JsonSerializerSettings DeserializeSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            }
        };

        /// <summary>
        /// Serialize a SyncSnapshot to indented JSON string.
        /// </summary>
        public static string Serialize(SyncSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.SchemaVersion != 2)
                throw new FormatException(
                    $"Unsupported schemaVersion: {snapshot.SchemaVersion}. Expected: 2.");

            ValidateV2Snapshot(snapshot);

            var canonical = new SyncSnapshot
            {
                SchemaVersion = snapshot.SchemaVersion,
                DeviceId = snapshot.DeviceId,
                ExportedAt = snapshot.ExportedAt,
                Playlists = snapshot.Playlists ?? new List<SyncPlaylist>(),
                LoopPoints = snapshot.LoopPoints ?? new List<SyncLoopPointEntry>(),
                Ratings = snapshot.Ratings ?? new List<SyncRatingEntry>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Canonicalize(snapshot.PlaybackStatistics)
            };
            return JsonConvert.SerializeObject(canonical, SerializeSettings);
        }

        /// <summary>
        /// Deserialize JSON into SyncSnapshot with validation.
        /// Throws FormatException if schemaVersion is not 2, required fields are missing,
        /// or v2 playback statistics are invalid.
        /// Null lists are normalized to empty lists.
        /// </summary>
        public static SyncSnapshot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("JSON input is null or empty.");

            SyncSnapshot snapshot;
            try
            {
                using (var stringReader = new StringReader(json))
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    var token = Newtonsoft.Json.Linq.JToken.Load(jsonReader, new Newtonsoft.Json.Linq.JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error
                    });
                    snapshot = token.ToObject<SyncSnapshot>(JsonSerializer.Create(DeserializeSettings));
                }
            }
            catch (JsonException ex)
            {
                throw new FormatException("Invalid JSON input.", ex);
            }

            if (snapshot == null)
                throw new FormatException("Deserialization returned null.");

            if (snapshot.SchemaVersion != 2)
                throw new FormatException(
                    $"Unsupported schemaVersion: {snapshot.SchemaVersion}. Expected: 2.");

            // Normalize null lists to empty
            snapshot.Playlists ??= new List<SyncPlaylist>();
            snapshot.LoopPoints ??= new List<SyncLoopPointEntry>();
            snapshot.Ratings ??= new List<SyncRatingEntry>();

            ValidateV2Snapshot(snapshot);

            return snapshot;
        }

        internal static void ValidateV2Snapshot(SyncSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.SchemaVersion != 2)
                throw new FormatException(
                    $"Unsupported schemaVersion: {snapshot.SchemaVersion}. Expected: 2.");
            if (string.IsNullOrWhiteSpace(snapshot.DeviceId))
                throw new FormatException("Missing required field: deviceId.");
            if (snapshot.ExportedAt < 0)
                throw new FormatException("exportedAt must be >= 0.");

            ValidatePlaybackStatistics(snapshot.PlaybackStatistics);
        }

        public static string NormalizePlaybackSongFileName(string fileName)
        {
            if (fileName == null) return null;
            var slashSafe = fileName.Replace('\\', '/');
            var baseName = Path.GetFileName(slashSafe);
            return baseName.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static void ValidatePlaybackStatistics(SyncPlaybackStatistics statistics)
        {
            if (statistics == null || statistics.Devices == null || statistics.Songs == null || statistics.Tombstones == null)
                throw new FormatException("schemaVersion 2 requires playbackStatistics devices, songs, and tombstones containers.");
            if (statistics.DateBucketBasis != SourceLocalDateBucketBasis)
                throw new FormatException($"Unsupported dateBucketBasis: {statistics.DateBucketBasis}.");

            var devices = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in statistics.Devices)
            {
                if (device == null || string.IsNullOrWhiteSpace(device.DeviceId)) throw new FormatException("Playback deviceId is required.");
                if (string.IsNullOrWhiteSpace(device.DisplayName)) throw new FormatException("Playback device displayName is required and must not be blank.");
                if (device.CurrentGeneration < 0 || device.FirstSeenAtUtcMs < 0 || device.LastSeenAtUtcMs < 0) throw new FormatException("Playback device generation and timestamps must be >= 0.");
                if (device.DisplayNameUpdatedAtUtcMs <= 0) throw new FormatException("Playback device displayNameUpdatedAtUtcMs must be positive.");
                if (device.Platform != "android" && device.Platform != "windows") throw new FormatException("Playback device platform must be android or windows.");
                if (device.FirstSeenAtUtcMs > device.LastSeenAtUtcMs) throw new FormatException("Playback device firstSeenAtUtcMs must not be after lastSeenAtUtcMs.");
                if (!devices.Add(device.DeviceId)) throw new FormatException($"Duplicate playback deviceId: {device.DeviceId}.");
            }

            var songKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var playbackSong in statistics.Songs)
            {
                if (playbackSong?.Song == null || playbackSong.Contributions == null) throw new FormatException("Playback song and contributions are required.");
                var song = playbackSong.Song;
                if (string.IsNullOrWhiteSpace(song.FileName) || string.IsNullOrWhiteSpace(song.NormalizedFileName)) throw new FormatException("Playback song identity is required.");
                if (song.DurationMs < 0 || (song.TotalSamples.HasValue && song.TotalSamples.Value < 0)) throw new FormatException("Playback song counters must be >= 0.");
                if (!string.Equals(song.NormalizedFileName, NormalizePlaybackSongFileName(song.FileName), StringComparison.Ordinal)) throw new FormatException("Playback normalizedFileName does not match fileName.");
                var songKey = song.NormalizedFileName + "\u001f" + song.DurationMs.ToString(CultureInfo.InvariantCulture);
                if (!songKeys.Add(songKey)) throw new FormatException("Duplicate playback song key.");

                var contributionKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var contribution in playbackSong.Contributions)
                {
                    if (contribution == null || string.IsNullOrWhiteSpace(contribution.DeviceId) || contribution.DatedListenMs == null) throw new FormatException("Playback contribution fields are required.");
                    if (!devices.Contains(contribution.DeviceId)) throw new FormatException("Playback contribution references an unregistered device.");
                    if (contribution.Generation < 0 || contribution.UndatedListenMs < 0 || contribution.UpdatedAtUtcMs < 0 || (contribution.FirstPlayedAtUtcMs.HasValue && contribution.FirstPlayedAtUtcMs.Value < 0) || (contribution.LastPlayedAtUtcMs.HasValue && contribution.LastPlayedAtUtcMs.Value < 0)) throw new FormatException("Playback contribution counters and timestamps must be >= 0.");
                    if (contribution.FirstPlayedAtUtcMs.GetValueOrDefault() != 0 && contribution.LastPlayedAtUtcMs.GetValueOrDefault() != 0 && contribution.FirstPlayedAtUtcMs > contribution.LastPlayedAtUtcMs) throw new FormatException("Playback contribution firstPlayedAtUtcMs must not be after lastPlayedAtUtcMs.");
                    if (!contributionKeys.Add(contribution.DeviceId + "\u001f" + contribution.Generation.ToString(CultureInfo.InvariantCulture))) throw new FormatException("Duplicate playback contribution key.");
                    foreach (var dated in contribution.DatedListenMs)
                        if (!IsExactDate(dated.Key) || dated.Value < 0) throw new FormatException("Playback datedListenMs requires valid yyyy-MM-dd dates and non-negative counters.");
                }
            }

            var tombstoneKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tombstone in statistics.Tombstones)
            {
                if (tombstone == null || string.IsNullOrWhiteSpace(tombstone.DeviceId) || string.IsNullOrWhiteSpace(tombstone.TombstonedByDeviceId) || string.IsNullOrWhiteSpace(tombstone.Reason)) throw new FormatException("Playback tombstone deviceId, tombstonedByDeviceId, and reason are required.");
                if (!devices.Contains(tombstone.DeviceId)) throw new FormatException("Playback tombstone references an unregistered device.");
                if (!devices.Contains(tombstone.TombstonedByDeviceId)) throw new FormatException("Playback tombstone actor references an unregistered device.");
                if (tombstone.Generation < 0 || tombstone.TombstonedAtUtcMs < 0) throw new FormatException("Playback tombstone counters and timestamps must be >= 0.");
                if (tombstone.Scope != DeviceGenerationTombstoneScope) throw new FormatException($"Unsupported playback tombstone scope: {tombstone.Scope}.");
                if (!tombstoneKeys.Add(tombstone.DeviceId + "\u001f" + tombstone.Generation.ToString(CultureInfo.InvariantCulture) + "\u001f" + tombstone.Scope)) throw new FormatException("Duplicate playback tombstone key.");
            }
        }

        private static bool IsExactDate(string value)
        {
            return !string.IsNullOrEmpty(value) && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }
    }
}
