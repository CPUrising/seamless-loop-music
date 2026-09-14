using System.Threading;
using System.Threading.Tasks;

namespace seamless_loop_music.Services.Update
{
    /// <summary>
    /// Result of an update check against GitHub Releases.
    /// </summary>
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseNotes { get; set; }
        public string InstallerDownloadUrl { get; set; }
        public string InstallerAssetName { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsError => !string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Auto-update service: checks GitHub Releases, downloads the setup
    /// installer and applies the update by launching a silent install.
    /// </summary>
    public interface IAppUpdateService
    {
        /// <summary>
        /// Version embedded in the running assembly. Falls back to "0.0.0"
        /// when the assembly carries no explicit version (pre-1.11.0 builds).
        /// </summary>
        string GetCurrentVersion();

        /// <summary>
        /// Queries the latest GitHub Release and reports whether an update is available.
        /// </summary>
        Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default);

        /// <summary>
        /// Downloads the release installer to the temp update folder. Returns the local path.
        /// </summary>
        Task<string> DownloadInstallerAsync(UpdateCheckResult check, CancellationToken ct = default);

        /// <summary>
        /// Launches the downloaded installer in silent mode and shuts down the app.
        /// </summary>
        void ApplyUpdate(string installerPath);
    }
}
