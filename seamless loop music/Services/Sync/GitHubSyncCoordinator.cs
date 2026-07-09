using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Data;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// Orchestrates a full GitHub sync cycle: export local → download remote →
    /// merge → apply → upload with conflict retry.
    /// </summary>
    public class GitHubSyncCoordinator
    {
        private readonly ISyncSnapshotStore _store;
        private readonly ISyncBackend _backend;
        private readonly IDatabaseHelper _db;

        public GitHubSyncCoordinator(ISyncSnapshotStore store, ISyncBackend backend, IDatabaseHelper db)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Run a full sync cycle. Returns a detailed report.
        /// </summary>
        public async Task<GitHubSyncReport> SyncNowAsync(GitHubSyncConfig config,
            int maxConflictRetries = 3, CancellationToken ct = default)
        {
            var report = new GitHubSyncReport
            {
                LastSyncTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // ── 1. Validate config ──
            if (config == null || !config.IsConfigured)
            {
                report.Success = false;
                report.Status = "not_configured";
                report.ErrorMessage = "GitHub sync is not configured. Please set Owner, Repository, and Token.";
                return report;
            }

            try
            {
                // ── 2. Export local snapshot ──
                var localSnapshot = await _store.ExportSnapshotAsync().ConfigureAwait(false);

                // ── 3. Download remote snapshot ──
                RemoteSyncSnapshot remote;
                try
                {
                    remote = await _backend.DownloadAsync(config, ct).ConfigureAwait(false);
                }
                catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Unauthorized)
                {
                    report.Success = false;
                    report.Status = "unauthorized";
                    report.ErrorMessage = ex.Message;
                    return report;
                }

                // ── 4. Remote doesn't exist → initial upload ──
                if (!remote.Exists)
                {
                    try
                    {
                        var newSha = await _backend.UploadAsync(config, localSnapshot, null, ct).ConfigureAwait(false);
                        report.Success = true;
                        report.Status = "uploaded";
                        report.Uploaded = CountSnapshotEntities(localSnapshot);

                        SaveSyncState(newSha, report.LastSyncTime);
                        return report;
                    }
                    catch (SyncBackendException ex)
                    {
                        report.Success = false;
                        report.Status = "upload_failed";
                        report.ErrorMessage = ex.Message;
                        return report;
                    }
                }

                // ── 5. Remote exists → merge ──
                var mergeResult = SyncMergeEngine.Merge(localSnapshot, remote.Snapshot);
                var mergedSnapshot = mergeResult.Merged;
                report.Conflicts = mergeResult.Conflicts.Select(c => $"[{c.Field}] {c.Description}").ToList();

                // ── 6. Apply merged to local ──
                var applyResult = await _store.ApplySnapshotAsync(mergedSnapshot).ConfigureAwait(false);
                report.ApplyResult = applyResult;
                report.Applied = applyResult.AppliedLoopPoints + applyResult.AppliedRatings + applyResult.AppliedPlaylists;
                report.Downloaded = CountSnapshotEntities(remote.Snapshot);

                // ── 7. Upload merged with expectedRevision, handle conflicts ──
                string sha = remote.Revision;
                int retries = 0;
                SyncSnapshot currentLocal = localSnapshot;

                while (retries <= maxConflictRetries)
                {
                    try
                    {
                        sha = await _backend.UploadAsync(config, mergedSnapshot, sha, ct).ConfigureAwait(false);
                        // Upload succeeded
                        report.Success = true;
                        report.Status = retries > 0 ? "conflict_resolved" : "applied";
                        report.Uploaded = CountSnapshotEntities(mergedSnapshot);

                        SaveSyncState(sha, report.LastSyncTime);
                        return report;
                    }
                    catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Conflict && retries < maxConflictRetries)
                    {
                        retries++;

                        // Re-download fresh remote
                        try
                        {
                            remote = await _backend.DownloadAsync(config, ct).ConfigureAwait(false);
                        }
                        catch (SyncBackendException dex)
                        {
                            report.Success = false;
                            report.Status = "conflict_redownload_failed";
                            report.ErrorMessage = $"Conflict occurred and re-download failed: {dex.Message}";
                            return report;
                        }

                        if (!remote.Exists)
                        {
                            // Remote was deleted between retries → upload without expectedRevision
                            sha = null;
                            mergedSnapshot = currentLocal;
                            continue;
                        }

                        // Re-merge original local with new remote
                        mergeResult = SyncMergeEngine.Merge(currentLocal, remote.Snapshot);
                        mergedSnapshot = mergeResult.Merged;
                        sha = remote.Revision;

                        // Re-apply
                        applyResult = await _store.ApplySnapshotAsync(mergedSnapshot).ConfigureAwait(false);
                        report.ApplyResult = applyResult;
                        report.Applied += applyResult.AppliedLoopPoints + applyResult.AppliedRatings + applyResult.AppliedPlaylists;
                    }
                    catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Conflict)
                    {
                        // Exhausted retries
                        report.Success = false;
                        report.Status = "conflict_max_retries";
                        report.ErrorMessage = $"Upload conflict persisted after {maxConflictRetries} retries: {ex.Message}";
                        return report;
                    }
                }

                // Should not reach here
                report.Success = false;
                report.Status = "unknown";
                return report;
            }
            catch (SyncBackendException ex)
            {
                report.Success = false;
                report.Status = "backend_error";
                report.ErrorMessage = ex.Message;
                return report;
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Status = "unexpected_error";
                report.ErrorMessage = $"Unexpected error during sync: {ex.Message}";
                return report;
            }
        }

        // ──────────────────────────────────────────────
        //  Private helpers
        // ──────────────────────────────────────────────

        private void SaveSyncState(string sha, long syncTimeMs)
        {
            if (!string.IsNullOrEmpty(sha))
                _db.SetSetting("Sync.GitHub.LastRemoteSha", sha);
            _db.SetSetting("Sync.GitHub.LastSyncTime", syncTimeMs.ToString());
        }

        private static int CountSnapshotEntities(SyncSnapshot snap)
        {
            if (snap == null) return 0;
            return (snap.LoopPoints?.Count ?? 0) +
                   (snap.Ratings?.Count ?? 0) +
                   (snap.Playlists?.Count ?? 0);
        }
    }
}
