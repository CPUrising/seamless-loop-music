using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using seamless_loop_music.Data;

namespace seamless_loop_music.Services.Update
{
    /// <summary>
    /// Auto-update service backed by GitHub Releases. Installed via a silent
    /// Inno Setup install of the SeamlessLoopMusic-Setup-&lt;version&gt;.exe asset.
    /// </summary>
    public class AppUpdateService : IAppUpdateService
    {
        private const string UserAgent = "SeamlessLoopMusic";
        private const string ApiBase = "https://api.github.com";
        private const string DefaultOwner = "CPurising";
        private const string DefaultRepository = "seamless-loop-music";
        private const string SetupAssetPattern = "SeamlessLoopMusic-Setup-";

        private readonly IDatabaseHelper _db;
        private readonly HttpClient _http;

        public AppUpdateService(IDatabaseHelper db)
            : this(db, new HttpClient())
        {
        }

        public AppUpdateService(IDatabaseHelper db, HttpClient http)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <inheritdoc />
        public string GetCurrentVersion()
        {
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            // Pre-1.11.0 builds have no embedded version (defaults to 0.0.0.0).
            // Treat them as very old so any published release is offered.
            if (assemblyVersion == null ||
                (assemblyVersion.Major == 0 && assemblyVersion.Minor == 0 &&
                 assemblyVersion.Build == 0 && assemblyVersion.Revision == 0))
            {
                return "0.0.0";
            }

            return string.Format("{0}.{1}.{2}", assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }

        /// <inheritdoc />
        public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
        {
            var current = GetCurrentVersion();

            try
            {
                var config = ReadConfig();
                if (string.IsNullOrWhiteSpace(config.Owner) || string.IsNullOrWhiteSpace(config.Repository))
                {
                    return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = "Update source not configured." };
                }

                var url = $"{ApiBase}/repos/{Uri.EscapeDataString(config.Owner)}/{Uri.EscapeDataString(config.Repository)}/releases/latest";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", UserAgent);
                request.Headers.Add("Accept", "application/vnd.github+json");

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = $"Network error: {ex.Message}" };
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = "No releases found." };
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = $"GitHub returned {response.StatusCode}." };
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = $"GitHub returned {response.StatusCode}." };
                    }

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseRelease(current, json);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = ex.Message };
            }
        }

        /// <inheritdoc />
        public async Task<string> DownloadInstallerAsync(UpdateCheckResult check, CancellationToken ct = default)
        {
            if (check == null || string.IsNullOrEmpty(check.InstallerDownloadUrl))
                throw new InvalidOperationException("No installer download URL available.");

            var updateDir = Path.Combine(Path.GetTempPath(), "SeamlessLoopMusicUpdate");
            Directory.CreateDirectory(updateDir);

            var fileName = string.IsNullOrWhiteSpace(check.InstallerAssetName)
                ? $"SeamlessLoopMusic-Setup-{check.LatestVersion}.exe"
                : check.InstallerAssetName;
            var localPath = Path.Combine(updateDir, fileName);

            using var request = new HttpRequestMessage(HttpMethod.Get, check.InstallerDownloadUrl);
            request.Headers.Add("User-Agent", UserAgent);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Download failed: {response.StatusCode}");

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var file = File.Create(localPath))
            {
                await stream.CopyToAsync(file, 81920, ct).ConfigureAwait(false);
            }

            return localPath;
        }

        /// <inheritdoc />
        public void ApplyUpdate(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                throw new FileNotFoundException("Installer not found.", installerPath);

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /AUTOLAUNCH",
                UseShellExecute = true
            };

            Process.Start(psi);

            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(() => app.Shutdown());
            }
            else
            {
                app?.Shutdown();
            }
        }

        // ──────────────────────────────────────────────
        //  Private helpers
        // ──────────────────────────────────────────────

        private (string Owner, string Repository) ReadConfig()
        {
            var owner = _db.GetSetting("Update.GitHub.Owner") ?? "";
            var repository = _db.GetSetting("Update.GitHub.Repository") ?? "";
            if (string.IsNullOrWhiteSpace(owner)) owner = DefaultOwner;
            if (string.IsNullOrWhiteSpace(repository)) repository = DefaultRepository;
            return (owner, repository);
        }

        private UpdateCheckResult ParseRelease(string current, string json)
        {
            JObject payload;
            try
            {
                payload = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = $"Invalid response: {ex.Message}" };
            }

            var tagName = payload["tag_name"]?.ToString();
            var latest = SemanticVersion.FromTag(tagName);

            if (latest == null)
                return new UpdateCheckResult { CurrentVersion = current, ErrorMessage = $"Unrecognized release tag: {tagName}" };

            var notes = payload["body"]?.ToString() ?? "";
            if (notes.Length > 4000) notes = notes.Substring(0, 4000) + "...";

            string downloadUrl = null;
            string assetName = null;
            var assets = payload["assets"] as JArray;
            if (assets != null)
            {
                var match = assets.FirstOrDefault(a =>
                {
                    var name = a["name"]?.ToString() ?? "";
                    return name.StartsWith(SetupAssetPattern, StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                });

                if (match != null)
                {
                    assetName = match["name"]?.ToString();
                    downloadUrl = match["browser_download_url"]?.ToString();
                }
            }

            var currentSemVer = SemanticVersion.FromString(current);

            return new UpdateCheckResult
            {
                UpdateAvailable = SemanticVersion.IsNewer(latest, currentSemVer),
                CurrentVersion = current,
                LatestVersion = latest.Text,
                ReleaseNotes = notes,
                InstallerDownloadUrl = downloadUrl,
                InstallerAssetName = assetName
            };
        }
    }
}
