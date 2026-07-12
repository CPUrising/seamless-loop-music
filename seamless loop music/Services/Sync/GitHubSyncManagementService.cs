using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    public class GitHubSyncManagementService : IGitHubSyncManagementService
    {
        private readonly IDatabaseHelper _db;
        private readonly ISyncBackend _backend;
        private readonly IGitHubSyncPreparationService _preparation;

        public GitHubSyncManagementService(IDatabaseHelper db, ISyncBackend backend, IGitHubSyncPreparationService preparation)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        }

        // ──────────────────────────────────────────
        //  Refresh overview
        // ──────────────────────────────────────────

        public async Task<SyncDataOverview> RefreshOverviewAsync(CancellationToken ct = default)
        {
            try
            {
                return await Task.Run(async () =>
                {
                    ct.ThrowIfCancellationRequested();

                    var overview = new SyncDataOverview();

                    // ── Local summary ──
                    overview.Local = BuildLocalSummary();

                    // ── Cloud ──
                    var config = GetConfig();
                    if (!config.IsConfigured)
                    {
                        overview.Status = "not_configured";
                        return overview;
                    }

                    RemoteSyncSnapshot remote;
                    try
                    {
                        remote = await _backend.DownloadAsync(config, ct).ConfigureAwait(false);
                    }
                    catch (SyncBackendException ex)
                    {
                        overview.Status = "backend_error";
                        overview.ErrorMessage = ex.Message;
                        return overview;
                    }

                    if (!remote.Exists || remote.Snapshot == null)
                    {
                        overview.CloudExists = false;
                        return overview;
                    }

                    overview.CloudExists = true;
                    var snap = remote.Snapshot;

                    // ── Cloud summary ──
                    overview.Cloud = new CloudSyncDataSummary
                    {
                        PlaylistCount = snap.Playlists?.Count ?? 0,
                        LoopPointCount = snap.LoopPoints?.Count ?? 0,
                        RatingCount = snap.Ratings?.Count ?? 0
                    };

                    // ── Distinct cloud song references ──
                    var cloudRefs = CollectCloudSongReferences(snap);
                    overview.Cloud.SongReferenceCount = cloudRefs.Count;

                    // ── Match local tracks ──
                    MatchCloudReferences(cloudRefs, overview);

                    return overview;
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SyncDataOverview
                {
                    Status = "cancelled"
                };
            }
        }

        // ──────────────────────────────────────────
        //  Force push local → cloud
        // ──────────────────────────────────────────

        public async Task<SyncManagementOperationResult> ForcePushLocalToCloudAsync(CancellationToken ct = default)
        {
            var config = GetConfig();
            if (!config.IsConfigured)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "not_configured",
                    ErrorMessage = "GitHub sync is not configured."
                };
            }

            // Download remote to get current SHA (404 → sha=null)
            string expectedRevision = null;
            RemoteSyncSnapshot remote = null;
            try
            {
                remote = await _backend.DownloadAsync(config, ct).ConfigureAwait(false);
                if (remote.Exists)
                    expectedRevision = remote.Revision;
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Unauthorized)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "unauthorized",
                    ErrorMessage = ex.Message
                };
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Network)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "backend_error",
                    ErrorMessage = ex.Message
                };
            }
            catch (SyncBackendException ex)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "backend_error",
                    ErrorMessage = ex.Message
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "cancelled",
                    ErrorMessage = "GitHub sync was cancelled."
                };
            }

            try
            {
                if (remote != null && remote.Exists)
                    SyncSnapshotSerializer.ValidateV2Snapshot(remote.Snapshot);

                var prepared = await _preparation.PrepareForcePushAsync(
                    remote != null && remote.Exists ? remote.Snapshot : null, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var newSha = await _backend.UploadAsync(config, prepared.Outbound, expectedRevision, ct)
                    .ConfigureAwait(false);

                // Save metadata
                _db.SetSetting("Sync.GitHub.LastRemoteSha", newSha);
                _db.SetSetting("Sync.GitHub.LastSyncTime",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

                return new SyncManagementOperationResult
                {
                    Success = true,
                    Status = "uploaded",
                    Revision = newSha
                };
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Conflict)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "conflict",
                    ErrorMessage = ex.Message
                };
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Unauthorized)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "unauthorized",
                    ErrorMessage = ex.Message
                };
            }
            catch (SyncBackendException ex)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "backend_error",
                    ErrorMessage = ex.Message
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "cancelled",
                    ErrorMessage = "GitHub sync was cancelled."
                };
            }
            catch (InvalidOperationException ex)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "preparation_failed",
                    ErrorMessage = ex.Message
                };
            }
            catch (Exception ex)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "preparation_failed",
                    ErrorMessage = ex.Message
                };
            }
        }

        // ──────────────────────────────────────────
        //  Delete cloud snapshot
        // ──────────────────────────────────────────

        public async Task<SyncManagementOperationResult> DeleteCloudSnapshotAsync(CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var config = GetConfig();
                if (!config.IsConfigured)
                {
                    return new SyncManagementOperationResult
                    {
                        Success = false,
                        Status = "not_configured",
                        ErrorMessage = "GitHub sync is not configured."
                    };
                }

                await _backend.DeleteAsync(config, ct).ConfigureAwait(false);

                // Clear saved sync metadata
                _db.SetSetting("Sync.GitHub.LastRemoteSha", "");
                _db.SetSetting("Sync.GitHub.LastSyncTime", "");

                return new SyncManagementOperationResult
                {
                    Success = true,
                    Status = "deleted"
                };
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Unauthorized)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "unauthorized",
                    ErrorMessage = ex.Message
                };
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Conflict)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "conflict",
                    ErrorMessage = ex.Message
                };
            }
            catch (SyncBackendException ex)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "backend_error",
                    ErrorMessage = ex.Message
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "cancelled"
                };
            }
        }

        // ──────────────────────────────────────────
        //  Clear local sync data
        // ──────────────────────────────────────────

        public async Task<SyncManagementOperationResult> ClearLocalSyncDataAsync(
            ClearLocalSyncDataSelection selection, CancellationToken ct = default)
        {
            try
            {
                return await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    if (selection == null || !selection.HasAny)
                    {
                        return new SyncManagementOperationResult
                        {
                            Success = false,
                            Status = "no_selection",
                            ErrorMessage = "No data categories selected to clear."
                        };
                    }

                    ct.ThrowIfCancellationRequested();
                    int affected = 0;

                    using (var conn = _db.GetConnection())
                    using (var trans = conn.BeginTransaction())
                    {
                        if (selection.ClearPlaylists)
                        {
                            ct.ThrowIfCancellationRequested();
                            // Delete playlist items first (though cascade should handle it)
                            conn.Execute("DELETE FROM PlaylistItems", transaction: trans);
                            ct.ThrowIfCancellationRequested();
                            int plCount = conn.Execute("DELETE FROM Playlists", transaction: trans);
                            affected += plCount;

                            ct.ThrowIfCancellationRequested();
                            // Clean playlist sync-id mappings from AppSettings
                            conn.Execute(
                                "DELETE FROM AppSettings WHERE Key LIKE 'Sync.PlaylistId.%' OR Key LIKE 'Sync.PlaylistLocalId.%'",
                                transaction: trans);
                        }

                        if (selection.ClearLoopPoints)
                        {
                            ct.ThrowIfCancellationRequested();
                            int lpCount = conn.Execute("DELETE FROM LoopPoints", transaction: trans);
                            affected += lpCount;
                        }

                        if (selection.ClearRatings)
                        {
                            ct.ThrowIfCancellationRequested();
                            int rtCount = conn.Execute("DELETE FROM UserRatings", transaction: trans);
                            affected += rtCount;
                        }

                        ct.ThrowIfCancellationRequested();
                        trans.Commit();
                    }

                    // Complete metadata cleanup after the transaction commits so cancellation
                    // cannot leave the database domains in a partially-cleared state.
                    _db.SetSetting("Sync.GitHub.LastRemoteSha", "");
                    _db.SetSetting("Sync.GitHub.LastSyncTime", "");

                    return new SyncManagementOperationResult
                    {
                        Success = true,
                        Status = "cleared",
                        AffectedCount = affected
                    };
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SyncManagementOperationResult
                {
                    Success = false,
                    Status = "cancelled"
                };
            }
        }

        // ──────────────────────────────────────────
        //  Private helpers
        // ──────────────────────────────────────────

        private GitHubSyncConfig GetConfig()
        {
            return new GitHubSyncConfig
            {
                Owner = _db.GetSetting("Sync.GitHub.Owner") ?? "",
                Repository = _db.GetSetting("Sync.GitHub.Repository") ?? "",
                Branch = _db.GetSetting("Sync.GitHub.Branch") ?? "main",
                Path = _db.GetSetting("Sync.GitHub.Path") ?? "seamless-loop/sync.json",
                Token = _db.GetSetting("Sync.GitHub.Token") ?? ""
            };
        }

        private SyncDataSummary BuildLocalSummary()
        {
            using var conn = _db.GetConnection();

            var summary = new SyncDataSummary();

            summary.SongCount = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Tracks");
            summary.PlaylistCount = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Playlists");

            // Substantial loop points: exclude 0/0 and 0→TotalSamples (desktop default)
            summary.LoopPointCount = conn.ExecuteScalar<int>(@"
                SELECT COUNT(1) FROM LoopPoints lp
                JOIN Tracks t ON lp.TrackId = t.Id
                WHERE (lp.LoopStart != 0 OR lp.LoopEnd != 0)
                  AND NOT (lp.LoopStart = 0 AND lp.LoopEnd = t.TotalSamples AND t.TotalSamples > 0)
            ");

            // Non-zero ratings
            summary.RatingCount = conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM UserRatings WHERE Rating IS NOT NULL AND Rating != 0");

            return summary;
        }

        /// <summary>
        /// Collect distinct cloud song references from loopPoints, ratings, and playlist items.
        /// Uses the same identity-key logic as SyncMergeEngine.GetIdentityKey for dedup.
        /// </summary>
        private static HashSet<string> CollectCloudSongReferences(SyncSnapshot snap)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var lp in snap.LoopPoints ?? Enumerable.Empty<SyncLoopPointEntry>())
            {
                if (lp.Song != null)
                    refs.Add(SyncMergeEngine.GetIdentityKey(lp.Song));
            }

            foreach (var rt in snap.Ratings ?? Enumerable.Empty<SyncRatingEntry>())
            {
                if (rt.Song != null)
                    refs.Add(SyncMergeEngine.GetIdentityKey(rt.Song));
            }

            foreach (var pl in snap.Playlists ?? Enumerable.Empty<SyncPlaylist>())
            {
                foreach (var item in pl.Items ?? Enumerable.Empty<SyncPlaylistItem>())
                {
                    if (item.Song != null)
                        refs.Add(SyncMergeEngine.GetIdentityKey(item.Song));
                }
            }

            return refs;
        }

        /// <summary>
        /// Count how many cloud song references can be matched to local tracks
        /// using the same 4-tier algorithm as SQLiteSyncSnapshotStore.
        /// </summary>
        private void MatchCloudReferences(HashSet<string> cloudRefKeys, SyncDataOverview overview)
        {
            if (cloudRefKeys.Count == 0)
            {
                overview.MatchedCloudSongReferences = 0;
                overview.MissingCloudSongReferences = 0;
                return;
            }

            // Load local tracks for matching
            using var conn = _db.GetConnection();
            var localTracks = conn.Query<SQLiteSyncSnapshotStore.LocalTrackDto>(@"
                SELECT Id, FileName, TotalSamples, DurationMs FROM Tracks
            ").ToList();

            // Reconstruct SyncSongIdentity from each key and try to match.
            // The identity key format is: "fileName|dur:durationMs" or "fileName|smp:totalSamples" or "fileName".
            // We need to reverse this to try matching. Since the key already encodes
            // the best available identity, we reconstruct a minimal SyncSongIdentity.
            int matched = 0;
            int missing = 0;

            foreach (var key in cloudRefKeys)
            {
                var identity = ReconstructIdentityFromKey(key);
                if (identity == null) { missing++; continue; }

                var match = SQLiteSyncSnapshotStore.MatchTrack(identity, localTracks);
                if (match != null && match.Track != null && match.Conflicts == 1)
                    matched++;
                else
                    missing++;
            }

            overview.MatchedCloudSongReferences = matched;
            overview.MissingCloudSongReferences = missing;
        }

        /// <summary>
        /// Reverse a SyncMergeEngine identity key into a SyncSongIdentity for matching.
        /// Key format: "fileName" or "fileName|dur:12345" or "fileName|smp:67890".
        /// </summary>
        private static SyncSongIdentity ReconstructIdentityFromKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            int pipeIdx = key.IndexOf('|');
            if (pipeIdx < 0)
            {
                // Just fileName, no duration or samples info
                return new SyncSongIdentity { FileName = key };
            }

            var fileName = key.Substring(0, pipeIdx);
            var suffix = key.Substring(pipeIdx + 1);

            if (suffix.StartsWith("dur:"))
            {
                if (long.TryParse(suffix.Substring(4), out var dur) && dur > 0)
                    return new SyncSongIdentity { FileName = fileName, DurationMs = dur };
            }
            else if (suffix.StartsWith("smp:"))
            {
                if (long.TryParse(suffix.Substring(4), out var smp) && smp > 0)
                    return new SyncSongIdentity { FileName = fileName, TotalSamples = smp };
            }

            return new SyncSongIdentity { FileName = fileName };
        }
    }
}
