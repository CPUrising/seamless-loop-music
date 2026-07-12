using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    internal sealed class FakeSyncPreparationService : IGitHubSyncPreparationService
    {
        private readonly ISyncSnapshotStore _store;

        public FakeSyncPreparationService(ISyncSnapshotStore store)
        {
            _store = store;
        }

        public int CaptureCount { get; private set; }
        public Func<int, Task<SyncSnapshot>>? CaptureFunc { get; set; }

        public async Task<SyncSnapshot> CaptureFreshLocalSnapshotAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CaptureCount++;
            return CaptureFunc != null ? await CaptureFunc(CaptureCount) : await _store.ExportSnapshotAsync();
        }

        public async Task<PreparedSyncSnapshot> PrepareNormalSyncAsync(SyncSnapshot remoteSnapshot, CancellationToken ct = default)
        {
            var local = await CaptureFreshLocalSnapshotAsync(ct);
            var first = SyncMergeEngine.Merge(local, remoteSnapshot);
            var apply = await _store.ApplySnapshotAsync(first.Merged);
            var fresh = await CaptureFreshLocalSnapshotAsync(ct);
            var final = SyncMergeEngine.Merge(fresh, remoteSnapshot);
            return new PreparedSyncSnapshot
            {
                Outbound = final.Merged,
                ApplyResult = apply,
                Conflicts = first.Conflicts
            };
        }

        public async Task<PreparedSyncSnapshot> PrepareForcePushAsync(SyncSnapshot remoteSnapshot, CancellationToken ct = default)
        {
            var local = await CaptureFreshLocalSnapshotAsync(ct);
            return new PreparedSyncSnapshot
            {
                Outbound = local,
                ApplyResult = new SyncApplyResult(),
                Conflicts = new List<SyncMergeConflict>()
            };
        }
    }
}
