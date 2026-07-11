using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    public interface IGitHubSyncPreparationService
    {
        Task<SyncSnapshot> CaptureFreshLocalSnapshotAsync(CancellationToken ct = default);
        Task<PreparedSyncSnapshot> PrepareNormalSyncAsync(SyncSnapshot remoteSnapshot, CancellationToken ct = default);
        Task<PreparedSyncSnapshot> PrepareForcePushAsync(SyncSnapshot remoteSnapshot, CancellationToken ct = default);
    }

    public sealed class PreparedSyncSnapshot
    {
        public SyncSnapshot Outbound { get; set; }
        public SyncApplyResult ApplyResult { get; set; }
        public List<SyncMergeConflict> Conflicts { get; set; } = new List<SyncMergeConflict>();
    }
}
