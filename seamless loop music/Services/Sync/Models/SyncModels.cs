using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace seamless_loop_music.Services.Sync.Models
{
    // ──────────────────────────────────────────────
    //  Sync main DTOs (phone-compatible schema v1)
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
