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
    /// Orchestrates a full GitHub sync cycle: download remote, prepare a fenced
    /// v2 outbound snapshot, then upload with conflict retry.
    /// </summary>
    public class GitHubSyncCoordinator
    {
        private readonly ISyncBackend _backend;
        private readonly IDatabaseHelper _db;
        private readonly IGitHubSyncPreparationService _preparation;

        public GitHubSyncCoordinator(ISyncBackend backend, IDatabaseHelper db, IGitHubSyncPreparationService preparation)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        }

        public async Task<GitHubSyncReport> SyncNowAsync(GitHubSyncConfig config,
            int maxConflictRetries = 3, CancellationToken ct = default)
        {
            var report = new GitHubSyncReport
            {
                LastSyncTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (config == null || !config.IsConfigured)
            {
                report.Success = false;
                report.Status = "not_configured";
                report.ErrorMessage = "GitHub sync is not configured. Please set Owner, Repository, and Token.";
                return report;
            }

            try
            {
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

                SyncSnapshot outbound;
                SyncApplyResult applyResult = null;
                if (!remote.Exists)
                {
                    outbound = await _preparation.CaptureFreshLocalSnapshotAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    var prepared = await _preparation.PrepareNormalSyncAsync(remote.Snapshot, ct).ConfigureAwait(false);
                    outbound = prepared.Outbound;
                    applyResult = prepared.ApplyResult;
                    ApplyReport(report, applyResult, prepared.Conflicts);
                    report.Downloaded = CountSnapshotEntities(remote.Snapshot);
                }

                string sha = remote.Exists ? remote.Revision : null;
                var retries = 0;
                while (retries <= maxConflictRetries)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        sha = await _backend.UploadAsync(config, outbound, sha, ct).ConfigureAwait(false);
                        report.Success = true;
                        report.Status = retries > 0 ? "conflict_resolved" : (remote.Exists ? "applied" : "uploaded");
                        report.Uploaded = CountSnapshotEntities(outbound);
                        SaveSyncState(sha, report.LastSyncTime);
                        return report;
                    }
                    catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Conflict && retries < maxConflictRetries)
                    {
                        retries++;

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
                            outbound = await _preparation.CaptureFreshLocalSnapshotAsync(ct).ConfigureAwait(false);
                            sha = null;
                            continue;
                        }

                        var prepared = await _preparation.PrepareNormalSyncAsync(remote.Snapshot, ct).ConfigureAwait(false);
                        outbound = prepared.Outbound;
                        sha = remote.Revision;
                        ApplyReport(report, prepared.ApplyResult, prepared.Conflicts);
                        report.Downloaded = CountSnapshotEntities(remote.Snapshot);
                    }
                    catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Conflict)
                    {
                        report.Success = false;
                        report.Status = "conflict_max_retries";
                        report.ErrorMessage = $"Upload conflict persisted after {maxConflictRetries} retries: {ex.Message}";
                        return report;
                    }
                }

                report.Success = false;
                report.Status = "unknown";
                return report;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                report.Success = false;
                report.Status = "cancelled";
                report.ErrorMessage = "GitHub sync was cancelled.";
                return report;
            }
            catch (SyncBackendException ex)
            {
                report.Success = false;
                report.Status = "backend_error";
                report.ErrorMessage = ex.Message;
                return report;
            }
            catch (InvalidOperationException ex)
            {
                report.Success = false;
                report.Status = "preparation_failed";
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

        private void SaveSyncState(string sha, long syncTimeMs)
        {
            if (!string.IsNullOrEmpty(sha))
                _db.SetSetting("Sync.GitHub.LastRemoteSha", sha);
            _db.SetSetting("Sync.GitHub.LastSyncTime", syncTimeMs.ToString());
        }

        private static void ApplyReport(GitHubSyncReport report, SyncApplyResult applyResult,
            System.Collections.Generic.IEnumerable<SyncMergeConflict> conflicts)
        {
            if (applyResult != null)
            {
                report.ApplyResult = applyResult;
                report.Applied += applyResult.AppliedLoopPoints + applyResult.AppliedRatings + applyResult.AppliedPlaylists;
            }
            if (conflicts != null)
                report.Conflicts = conflicts.Select(c => $"[{c.Field}] {c.Description}").ToList();
        }

        private static int CountSnapshotEntities(SyncSnapshot snap)
        {
            if (snap == null) return 0;
            var statistics = snap.PlaybackStatistics;
            return (snap.LoopPoints?.Count ?? 0) +
                   (snap.Ratings?.Count ?? 0) +
                   (snap.Playlists?.Count ?? 0) +
                   (statistics?.Devices?.Count ?? 0) +
                   (statistics?.Songs?.Count ?? 0) +
                   (statistics?.Tombstones?.Count ?? 0);
        }
    }
}
