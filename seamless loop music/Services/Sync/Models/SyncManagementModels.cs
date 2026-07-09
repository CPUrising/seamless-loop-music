using System;

namespace seamless_loop_music.Services.Sync.Models
{
    // ──────────────────────────────────────────────
    //  Data summaries for overview display
    // ──────────────────────────────────────────────

    public class SyncDataSummary
    {
        public int SongCount { get; set; }
        public int PlaylistCount { get; set; }
        public int LoopPointCount { get; set; }   // substantial only (excl. 0/0 and 0→TotalSamples)
        public int RatingCount { get; set; }       // non-zero ratings
    }

    public class CloudSyncDataSummary
    {
        public int SongReferenceCount { get; set; } // distinct song identities across all sections
        public int PlaylistCount { get; set; }
        public int LoopPointCount { get; set; }
        public int RatingCount { get; set; }
    }

    public class SyncDataOverview
    {
        public SyncDataSummary Local { get; set; } = new SyncDataSummary();
        public CloudSyncDataSummary Cloud { get; set; } = new CloudSyncDataSummary();
        public int MatchedCloudSongReferences { get; set; }
        public int MissingCloudSongReferences { get; set; }
        public bool CloudExists { get; set; }
        public string Status { get; set; } = "ok";
        public string ErrorMessage { get; set; }
    }

    // ──────────────────────────────────────────────
    //  Clear-local selection
    // ──────────────────────────────────────────────

    public class ClearLocalSyncDataSelection
    {
        public bool ClearPlaylists { get; set; }
        public bool ClearLoopPoints { get; set; }
        public bool ClearRatings { get; set; }

        public bool HasAny => ClearPlaylists || ClearLoopPoints || ClearRatings;
    }

    // ──────────────────────────────────────────────
    //  Operation result
    // ──────────────────────────────────────────────

    public class SyncManagementOperationResult
    {
        public bool Success { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public int AffectedCount { get; set; }
        public string Revision { get; set; } // for uploads
    }
}
