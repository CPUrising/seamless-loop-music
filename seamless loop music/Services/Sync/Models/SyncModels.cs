using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace seamless_loop_music.Services.Sync.Models
{
    // ──────────────────────────────────────────────
    //  Sync main DTOs (cloud schema v2)
    // ──────────────────────────────────────────────

    public class SyncSnapshot
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("exportedAt")]
        public long ExportedAt { get; set; }

        [JsonProperty("playlists")]
        public List<SyncPlaylist> Playlists { get; set; }

        [JsonProperty("loopPoints")]
        public List<SyncLoopPointEntry> LoopPoints { get; set; }

        [JsonProperty("ratings")]
        public List<SyncRatingEntry> Ratings { get; set; }

        [JsonProperty("playbackStatistics", NullValueHandling = NullValueHandling.Ignore)]
        public SyncPlaybackStatistics PlaybackStatistics { get; set; }
    }

    // Schema v2 playback-statistics DTOs.
    public class SyncPlaybackStatistics
    {
        [JsonProperty("dateBucketBasis")]
        public string DateBucketBasis { get; set; }

        [JsonProperty("devices")]
        public List<SyncPlaybackDevice> Devices { get; set; }

        [JsonProperty("songs")]
        public List<SyncPlaybackSong> Songs { get; set; }

        [JsonProperty("tombstones")]
        public List<SyncPlaybackTombstone> Tombstones { get; set; }
    }

    public class SyncPlaybackDevice
    {
        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("currentGeneration")]
        public long CurrentGeneration { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("displayNameUpdatedAtUtcMs")]
        public long DisplayNameUpdatedAtUtcMs { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("firstSeenAtUtcMs")]
        public long FirstSeenAtUtcMs { get; set; }

        [JsonProperty("lastSeenAtUtcMs")]
        public long LastSeenAtUtcMs { get; set; }
    }

    public class SyncPlaybackSong
    {
        [JsonProperty("song")]
        public SyncPlaybackSongIdentity Song { get; set; }

        [JsonProperty("contributions")]
        public List<SyncPlaybackContribution> Contributions { get; set; }
    }

    public class SyncPlaybackSongIdentity
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("normalizedFileName")]
        public string NormalizedFileName { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("totalSamples", NullValueHandling = NullValueHandling.Ignore)]
        public long? TotalSamples { get; set; }

        [JsonProperty("contentHash", NullValueHandling = NullValueHandling.Ignore)]
        public string ContentHash { get; set; }
    }

    public class SyncPlaybackContribution
    {
        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("generation")]
        public long Generation { get; set; }

        [JsonProperty("datedListenMs")]
        public Dictionary<string, long> DatedListenMs { get; set; }

        [JsonProperty("undatedListenMs")]
        public long UndatedListenMs { get; set; }

        [JsonProperty("firstPlayedAtUtcMs")]
        public long? FirstPlayedAtUtcMs { get; set; }

        [JsonProperty("lastPlayedAtUtcMs")]
        public long? LastPlayedAtUtcMs { get; set; }

        [JsonProperty("updatedAtUtcMs")]
        public long UpdatedAtUtcMs { get; set; }
    }

    public class SyncPlaybackTombstone
    {
        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("generation")]
        public long Generation { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("tombstonedAtUtcMs")]
        public long TombstonedAtUtcMs { get; set; }

        [JsonProperty("tombstonedByDeviceId")]
        public string TombstonedByDeviceId { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    public class SyncSongIdentity
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("totalSamples", NullValueHandling = NullValueHandling.Ignore)]
        public long? TotalSamples { get; set; }
    }

    public class SyncPlaylist
    {
        [JsonProperty("id")]
        public string Id { get; set; } // UUID

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("createdAt")]
        public long CreatedAt { get; set; } // epoch ms

        [JsonProperty("modifiedAt")]
        public long ModifiedAt { get; set; } // epoch ms

        [JsonProperty("items")]
        public List<SyncPlaylistItem> Items { get; set; }
    }

    public class SyncPlaylistItem
    {
        [JsonProperty("song")]
        public SyncSongIdentity Song { get; set; }

        [JsonProperty("sortOrder")]
        public int SortOrder { get; set; }
    }

    public class SyncLoopPointEntry
    {
        [JsonProperty("song")]
        public SyncSongIdentity Song { get; set; }

        [JsonProperty("loopPoint")]
        public SyncLoopPoint LoopPoint { get; set; }
    }

    public class SyncLoopPoint
    {
        [JsonProperty("loopStart")]
        public long LoopStart { get; set; }

        [JsonProperty("loopEnd")]
        public long LoopEnd { get; set; }

        [JsonProperty("lastModified")]
        public long LastModified { get; set; } // epoch ms
    }

    public class SyncRatingEntry
    {
        [JsonProperty("song")]
        public SyncSongIdentity Song { get; set; }

        [JsonProperty("rating")]
        public SyncRating Rating { get; set; }
    }

    public class SyncRating
    {
        [JsonProperty("rating")]
        public int RatingValue { get; set; }

        [JsonProperty("lastModified")]
        public long LastModified { get; set; } // epoch ms
    }

    // ──────────────────────────────────────────────
    //  Apply result / report
    // ──────────────────────────────────────────────

    public class SyncApplyResult
    {
        public int AppliedLoopPoints { get; set; }
        public int SkippedLoopPoints { get; set; }
        public int AppliedRatings { get; set; }
        public int SkippedRatings { get; set; }
        public int AppliedPlaylists { get; set; }
        public int SkippedUnmatched { get; set; }
        public int SkippedAmbiguous { get; set; }
        public int TotalConflicts { get; set; }

        public override string ToString()
            => $"LoopPoints:{AppliedLoopPoints}+{SkippedLoopPoints} Ratings:{AppliedRatings}+{SkippedRatings} " +
               $"Playlists:{AppliedPlaylists} Unmatched:{SkippedUnmatched} Ambiguous:{SkippedAmbiguous} Conflicts:{TotalConflicts}";
    }

    // ──────────────────────────────────────────────
    //  Merge conflict item
    // ──────────────────────────────────────────────

    public class SyncMergeConflict
    {
        public string Field { get; set; } // "loopPoint", "rating", "playlist", "playlistItem"
        public string Description { get; set; }
    }

    public class SyncMergeResult
    {
        public SyncSnapshot Merged { get; set; }
        public List<SyncMergeConflict> Conflicts { get; set; } = new List<SyncMergeConflict>();
    }
}
