using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// Data management operations for GitHub sync:
    /// overview, force-push, delete cloud, clear local data.
    /// </summary>
    public interface IGitHubSyncManagementService
    {
        /// <summary>
        /// Refresh the sync data overview: local counts, cloud counts, matched/missing refs.
        /// </summary>
        Task<SyncDataOverview> RefreshOverviewAsync(CancellationToken ct = default);

        /// <summary>
        /// Overwrite cloud snapshot with local data (no merge).
        /// </summary>
        Task<SyncManagementOperationResult> ForcePushLocalToCloudAsync(CancellationToken ct = default);

        /// <summary>
        /// Delete the cloud sync file. 404 treated as success.
        /// </summary>
        Task<SyncManagementOperationResult> DeleteCloudSnapshotAsync(CancellationToken ct = default);

        /// <summary>
        /// Clear selected local sync data (playlists, loop points, ratings) in a transaction.
        /// Also cleans up playlist sync-id mappings from AppSettings.
        /// </summary>
        Task<SyncManagementOperationResult> ClearLocalSyncDataAsync(ClearLocalSyncDataSelection selection, CancellationToken ct = default);
    }
}
