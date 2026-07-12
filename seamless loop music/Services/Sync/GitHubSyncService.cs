using System;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Data;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// UI-facing GitHub sync service. Reads/writes config from AppSettings
    /// and delegates the sync cycle to GitHubSyncCoordinator.
    /// </summary>
    public class GitHubSyncService : IGitHubSyncService
    {
        private readonly IDatabaseHelper _db;
        private readonly GitHubSyncCoordinator _coordinator;

        public GitHubSyncService(IDatabaseHelper db, GitHubSyncCoordinator coordinator)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        /// <inheritdoc />
        public GitHubSyncConfig GetConfig()
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

        /// <inheritdoc />
        public void SaveConfig(GitHubSyncConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _db.SetSetting("Sync.GitHub.Owner", config.Owner ?? "");
            _db.SetSetting("Sync.GitHub.Repository", config.Repository ?? "");
            _db.SetSetting("Sync.GitHub.Branch", config.Branch ?? "main");
            _db.SetSetting("Sync.GitHub.Path", config.Path ?? "seamless-loop/sync.json");
            _db.SetSetting("Sync.GitHub.Token", config.Token ?? "");
        }

        /// <inheritdoc />
        public async Task<GitHubSyncReport> SyncNowAsync(CancellationToken ct = default)
        {
            var config = GetConfig();
            return await _coordinator.SyncNowAsync(config, maxConflictRetries: 3, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string GetLastSyncTimeDisplay()
        {
            var epochMsStr = _db.GetSetting("Sync.GitHub.LastSyncTime");
            if (string.IsNullOrEmpty(epochMsStr) || !long.TryParse(epochMsStr, out var epochMs) || epochMs <= 0)
                return "Never";

            try
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime;
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "Never";
            }
        }
    }
}
