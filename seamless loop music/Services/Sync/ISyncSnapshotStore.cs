using System.Threading.Tasks;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// Persistence for SyncSnapshot: export local state and apply merged snapshots.
    /// </summary>
    public interface ISyncSnapshotStore
    {
        /// <summary>
        /// Export current local DB state as a SyncSnapshot.
        /// </summary>
        Task<SyncSnapshot> ExportSnapshotAsync();

        /// <summary>
        /// Apply a merged snapshot to the local SQLite database.
        /// Returns detailed counts of what was applied/skipped.
        /// </summary>
        Task<SyncApplyResult> ApplySnapshotAsync(SyncSnapshot snapshot);
    }
}
