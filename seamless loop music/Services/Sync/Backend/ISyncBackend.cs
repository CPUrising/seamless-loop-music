using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync.Backend
{
    /// <summary>
    /// Remote sync backend abstraction for reading/writing/deleting snapshots on a remote store.
    /// </summary>
    public interface ISyncBackend
    {
        /// <summary>
        /// Download the remote snapshot. Returns Exists=false on 404 (not fatal).
        /// Throws/captures SyncBackendException on auth, network, or schema errors.
        /// </summary>
        Task<RemoteSyncSnapshot> DownloadAsync(GitHubSyncConfig config, CancellationToken ct = default);

        /// <summary>
        /// Upload a snapshot. If expectedRevision is non-empty, include it as the sha
        /// (so GitHub rejects the write if the remote has changed). Returns the new SHA.
        /// </summary>
        Task<string> UploadAsync(GitHubSyncConfig config, SyncSnapshot snapshot, string expectedRevision, CancellationToken ct = default);

        /// <summary>
        /// Delete the remote sync file from GitHub.
        /// 404 (file already gone) is treated as success.
        /// Throws SyncBackendException on auth, conflict, or network errors.
        /// </summary>
        Task DeleteAsync(GitHubSyncConfig config, CancellationToken ct = default);
    }
}
