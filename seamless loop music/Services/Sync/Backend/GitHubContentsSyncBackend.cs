using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync.Backend
{
    /// <summary>
    /// Sync backend backed by GitHub Contents API.
    /// GET/PUT /repos/{owner}/{repo}/contents/{path}.
    /// </summary>
    public class GitHubContentsSyncBackend : ISyncBackend
    {
        private const string BaseUrl = "https://api.github.com";
        private const string UserAgent = "SeamlessLoopMusic";
        private const string AcceptHeader = "application/vnd.github+json";

        private readonly HttpClient _http;

        public GitHubContentsSyncBackend()
            : this(new HttpClient())
        {
        }

        public GitHubContentsSyncBackend(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// Convenience ctor for test injection of HttpMessageHandler.
        /// </summary>
        public GitHubContentsSyncBackend(HttpMessageHandler handler)
            : this(new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))))
        {
        }

        /// <inheritdoc />
        public async Task<RemoteSyncSnapshot> DownloadAsync(GitHubSyncConfig config, CancellationToken ct = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var url = BuildDownloadUrl(config);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, config);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new SyncBackendException(SyncBackendCode.Network,
                    $"Network error downloading from GitHub: {ex.Message}", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new RemoteSyncSnapshot { Exists = false };
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SyncBackendException(SyncBackendCode.Unauthorized,
                        $"GitHub returned {response.StatusCode}. Check your token and permissions.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    throw new SyncBackendException(SyncBackendCode.Unknown,
                        $"GitHub GET returned {response.StatusCode}: {body}");
                }

                // 200 OK
                string json;
                try
                {
                    json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new SyncBackendException(SyncBackendCode.Network,
                        $"Failed to read GitHub response body: {ex.Message}", ex);
                }

                JObject payload;
                try
                {
                    payload = JObject.Parse(json);
                }
                catch (JsonReaderException ex)
                {
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        $"GitHub response is not valid JSON: {ex.Message}", ex);
                }

                var sha = payload["sha"]?.ToString();
                var contentB64 = payload["content"]?.ToString();

                if (string.IsNullOrWhiteSpace(sha))
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        "GitHub response missing 'sha' field.");

                if (string.IsNullOrWhiteSpace(contentB64))
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        "GitHub response missing 'content' field.");

                // Decode base64 (GitHub returns with newlines)
                string snapshotJson;
                try
                {
                    var cleaned = contentB64
                        .Replace("\r", "")
                        .Replace("\n", "")
                        .Replace(" ", "")
                        .Replace("\t", "");
                    var bytes = Convert.FromBase64String(cleaned);
                    snapshotJson = Encoding.UTF8.GetString(bytes);
                }
                catch (FormatException ex)
                {
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        $"Failed to decode base64 content: {ex.Message}", ex);
                }

                // Deserialize via SyncSnapshotSerializer
                SyncSnapshot snapshot;
                try
                {
                    snapshot = SyncSnapshotSerializer.Deserialize(snapshotJson);
                }
                catch (FormatException ex)
                {
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        $"Remote sync snapshot has invalid schema: {ex.Message}", ex);
                }

                return new RemoteSyncSnapshot
                {
                    Snapshot = snapshot,
                    Revision = sha,
                    Exists = true
                };
            }
        }

        /// <inheritdoc />
        public async Task<string> UploadAsync(GitHubSyncConfig config, SyncSnapshot snapshot,
            string expectedRevision, CancellationToken ct = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            // Validate and canonicalize before creating the request or contacting GitHub.
            var contentJson = SyncSnapshotSerializer.Serialize(snapshot);
            var contentB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(contentJson));

            var url = BuildUploadUrl(config);
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            ApplyHeaders(request, config);

            // Build body
            var bodyObj = new JObject
            {
                ["message"] = $"Sync snapshot from {snapshot.DeviceId} at {DateTime.UtcNow:O}",
                ["content"] = contentB64,
                ["branch"] = config.Branch ?? "main"
            };

            if (!string.IsNullOrEmpty(expectedRevision))
            {
                bodyObj["sha"] = expectedRevision;
            }

            request.Content = new StringContent(
                bodyObj.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new SyncBackendException(SyncBackendCode.Network,
                    $"Network error uploading to GitHub: {ex.Message}", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SyncBackendException(SyncBackendCode.Unauthorized,
                        $"GitHub returned {response.StatusCode}. Check your token and permissions.");
                }

                if (response.StatusCode == HttpStatusCode.Conflict ||
                    response.StatusCode == (HttpStatusCode)422)
                {
                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    throw new SyncBackendException(SyncBackendCode.Conflict,
                        $"GitHub write conflict: {body}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    throw new SyncBackendException(SyncBackendCode.Unknown,
                        $"GitHub PUT returned {response.StatusCode}: {body}");
                }

                // 200/201 Created
                string responseJson;
                try
                {
                    responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new SyncBackendException(SyncBackendCode.Network,
                        $"Failed to read GitHub PUT response: {ex.Message}", ex);
                }

                JObject resultPayload;
                try
                {
                    resultPayload = JObject.Parse(responseJson);
                }
                catch (JsonReaderException ex)
                {
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        $"GitHub PUT response is not valid JSON: {ex.Message}", ex);
                }

                // Prefer content.sha, fall back to commit.sha
                var newSha = resultPayload["content"]?["sha"]?.ToString()
                          ?? resultPayload["commit"]?["sha"]?.ToString();

                if (string.IsNullOrWhiteSpace(newSha))
                    throw new SyncBackendException(SyncBackendCode.InvalidRemote,
                        "GitHub PUT response missing content.sha and commit.sha");

                return newSha;
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(GitHubSyncConfig config, CancellationToken ct = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // First, GET to get the current SHA
            RemoteSyncSnapshot remote;
            try
            {
                remote = await DownloadAsync(config, ct).ConfigureAwait(false);
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Unauthorized)
            {
                throw; // let auth propagate
            }
            catch (SyncBackendException ex) when (ex.Code == SyncBackendCode.Network)
            {
                throw; // let network propagate
            }

            if (!remote.Exists || string.IsNullOrEmpty(remote.Revision))
            {
                // Already deleted — treat as success
                return;
            }

            var url = BuildUploadUrl(config);
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            ApplyHeaders(request, config);

            var bodyObj = new JObject
            {
                ["message"] = $"Delete sync snapshot ({remote.Revision})",
                ["sha"] = remote.Revision,
                ["branch"] = config.Branch ?? "main"
            };

            request.Content = new StringContent(
                bodyObj.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new SyncBackendException(SyncBackendCode.Network,
                    $"Network error deleting from GitHub: {ex.Message}", ex);
            }

            using (response)
            {
                // 404 means the file is already gone — success
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SyncBackendException(SyncBackendCode.Unauthorized,
                        $"GitHub returned {response.StatusCode}. Check your token and permissions.");
                }

                if (response.StatusCode == HttpStatusCode.Conflict ||
                    response.StatusCode == (HttpStatusCode)422)
                {
                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    throw new SyncBackendException(SyncBackendCode.Conflict,
                        $"GitHub delete conflict: {body}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    throw new SyncBackendException(SyncBackendCode.Unknown,
                        $"GitHub DELETE returned {response.StatusCode}: {body}");
                }

                // 200 OK — file deleted
            }
        }

        // ──────────────────────────────────────────────
        //  Private helpers
        // ──────────────────────────────────────────────

        private void ApplyHeaders(HttpRequestMessage request, GitHubSyncConfig config)
        {
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Add("Accept", AcceptHeader);
            if (!string.IsNullOrWhiteSpace(config.Token))
            {
                request.Headers.Add("Authorization", $"Bearer {config.Token}");
            }
        }

        private static string BuildDownloadUrl(GitHubSyncConfig config)
        {
            var path = EscapePath(config.Path ?? "seamless-loop/sync.json");
            return $"{BaseUrl}/repos/{EscapeSegment(config.Owner)}/{EscapeSegment(config.Repository)}/contents/{path}?ref={EscapeSegment(config.Branch ?? "main")}";
        }

        private static string BuildUploadUrl(GitHubSyncConfig config)
        {
            var path = EscapePath(config.Path ?? "seamless-loop/sync.json");
            return $"{BaseUrl}/repos/{EscapeSegment(config.Owner)}/{EscapeSegment(config.Repository)}/contents/{path}";
        }

        private static string EscapeSegment(string segment)
        {
            return Uri.EscapeDataString(segment ?? "");
        }

        /// <summary>
        /// Escape each path segment individually, preserving forward slashes.
        /// </summary>
        private static string EscapePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }

            return string.Join("/", parts);
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                return "(could not read body)";
            }
        }
    }
}
