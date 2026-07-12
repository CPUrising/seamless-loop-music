using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// UI-facing service for GitHub sync. Reads/writes config from AppSettings
    /// and delegates sync orchestration to GitHubSyncCoordinator.
    /// </summary>
    public interface IGitHubSyncService
    {
        /// <summary>
        /// Read the current GitHub sync configuration from AppSettings.
        /// </summary>
        GitHubSyncConfig GetConfig();

        /// <summary>
        /// Persist a GitHub sync configuration to AppSettings.
        /// </summary>
        void SaveConfig(GitHubSyncConfig config);

        /// <summary>
        /// Run a full sync cycle using the current configuration.
        /// </summary>
        Task<GitHubSyncReport> SyncNowAsync(CancellationToken ct = default);

        /// <summary>
        /// Read the last sync time from AppSettings as a human-friendly string,
        /// or "Never" if never synced.
        /// </summary>
        string GetLastSyncTimeDisplay();
    }
}
