using System;
using System.Collections.Generic;

namespace seamless_loop_music.Services.Sync.Models
{
    // ──────────────────────────────────────────────────────────────
    //  GitHub sync configuration
    // ──────────────────────────────────────────────────────────────

    public class GitHubSyncConfig
    {
        public string Owner { get; set; }
        public string Repository { get; set; }
        public string Branch { get; set; } = "main";
        public string Path { get; set; } = "seamless-loop/sync.json";
        public string Token { get; set; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Owner) &&
            !string.IsNullOrWhiteSpace(Repository) &&
            !string.IsNullOrWhiteSpace(Token);
    }

    // ──────────────────────────────────────────────────────────────
    //  Remote sync snapshot (backed by GitHub)
    // ──────────────────────────────────────────────────────────────

    public class RemoteSyncSnapshot
    {
        public SyncSnapshot Snapshot { get; set; }
        public string Revision { get; set; } // SHA from GitHub
        public bool Exists { get; set; } // true when remote file was found
    }

    // ──────────────────────────────────────────────────────────────
    //  Backend result codes
    // ──────────────────────────────────────────────────────────────

    public enum SyncBackendCode
    {
        Success,
        NotFound,
        Unauthorized,
        Conflict,
        InvalidRemote,
        Network,
        Unknown
    }

    public class SyncBackendResult
    {
        public SyncBackendCode Code { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsSuccess => Code == SyncBackendCode.Success;
    }

    // ──────────────────────────────────────────────────────────────
    //  Coordinator report
    // ──────────────────────────────────────────────────────────────

    public class GitHubSyncReport
    {
        public bool Success { get; set; }
        public string Status { get; set; } // "uploaded", "applied", "conflict_resolved", etc.
        public int Uploaded { get; set; }  // count of uploaded entities
        public int Downloaded { get; set; }
        public int Applied { get; set; }
        public SyncApplyResult ApplyResult { get; set; }
        public List<string> Conflicts { get; set; } = new List<string>();
        public string ErrorMessage { get; set; }
        public long LastSyncTime { get; set; } // epoch ms
    }
}
