using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    public sealed class GitHubSyncPreparationService : IGitHubSyncPreparationService
    {
        private readonly ISyncSnapshotStore _store;
        private readonly seamless_loop_music.Services.IPlaybackService _playbackService;

        public GitHubSyncPreparationService(ISyncSnapshotStore store, seamless_loop_music.Services.IPlaybackService playbackService)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        }

        public Task<SyncSnapshot> CaptureFreshLocalSnapshotAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return _playbackService.CapturePlaybackStatisticsCheckpointAsync(() => _store.ExportSnapshotAsync());
        }

        public async Task<PreparedSyncSnapshot> PrepareNormalSyncAsync(SyncSnapshot remoteSnapshot, CancellationToken ct = default)
        {
            if (remoteSnapshot == null) throw new ArgumentNullException(nameof(remoteSnapshot));

            var localSnapshot = await CaptureFreshLocalSnapshotAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var firstMerge = SyncMergeEngine.Merge(localSnapshot, remoteSnapshot);
            var applyResult = await _store.ApplySnapshotAsync(firstMerge.Merged).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await _playbackService.RotateIfCurrentGenerationTombstonedAsync().ConfigureAwait(false);

            var freshLocalSnapshot = await CaptureFreshLocalSnapshotAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var finalMerge = SyncMergeEngine.Merge(freshLocalSnapshot, remoteSnapshot);

            return new PreparedSyncSnapshot
            {
                Outbound = finalMerge.Merged,
                ApplyResult = applyResult,
                Conflicts = firstMerge.Conflicts ?? new List<SyncMergeConflict>()
            };
        }

        public async Task<PreparedSyncSnapshot> PrepareForcePushAsync(SyncSnapshot remoteSnapshot, CancellationToken ct = default)
        {
            if (remoteSnapshot != null)
                SyncSnapshotSerializer.ValidateV2Snapshot(remoteSnapshot);

            var localSnapshot = await CaptureFreshLocalSnapshotAsync(ct).ConfigureAwait(false);
            var applyResult = new SyncApplyResult();

            if (remoteSnapshot == null)
            {
                return new PreparedSyncSnapshot
                {
                    Outbound = localSnapshot,
                    ApplyResult = applyResult,
                    Conflicts = new List<SyncMergeConflict>()
                };
            }

            var statisticsOnly = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = localSnapshot.DeviceId ?? remoteSnapshot.DeviceId,
                ExportedAt = Math.Max(localSnapshot.ExportedAt, remoteSnapshot.ExportedAt),
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = remoteSnapshot.PlaybackStatistics
            };
            applyResult = await _store.ApplySnapshotAsync(statisticsOnly).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await _playbackService.RotateIfCurrentGenerationTombstonedAsync().ConfigureAwait(false);

            var freshLocalSnapshot = await CaptureFreshLocalSnapshotAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            freshLocalSnapshot.PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Merge(
                freshLocalSnapshot.PlaybackStatistics,
                remoteSnapshot.PlaybackStatistics);

            return new PreparedSyncSnapshot
            {
                Outbound = freshLocalSnapshot,
                ApplyResult = applyResult,
                Conflicts = new List<SyncMergeConflict>()
            };
        }

    }
}
