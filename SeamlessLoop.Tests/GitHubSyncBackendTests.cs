using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class GitHubSyncBackendTests
    {
        private const string SampleSha = "abc123def456";
        private const string NewSampleSha = "newsha789";

        /// <summary>
        /// Build a fake GitHub GET /contents response with base64-encoded snapshot.
        /// </summary>
        private static string BuildGetResponse(string sha, SyncSnapshot snapshot)
        {
            var json = SyncSnapshotSerializer.Serialize(snapshot);
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            // GitHub response wraps content with newlines at 60 chars
            var wrapped = string.Join("\n", Chunks(b64, 60));
            return $@"{{
                ""sha"": ""{sha}"",
                ""content"": ""{wrapped}""
            }}";
        }

        private static IEnumerable<string> Chunks(string s, int size)
        {
            for (int i = 0; i < s.Length; i += size)
                yield return s.Substring(i, Math.Min(size, s.Length - i));
        }

        private static SyncSnapshot MakeTestSnapshot()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "test-device",
                ExportedAt = 1700000000000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Test PL",
                        CreatedAt = 100, ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "a.mp3", DurationMs = 30000 },
                                SortOrder = 0
                            }
                        }
                    }
                },
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "b.mp3", DurationMs = 40000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 5000, LastModified = 300 }
                    }
                },
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "c.mp3", DurationMs = 50000 },
                        Rating = new SyncRating { RatingValue = 4, LastModified = 400 }
                    }
                },
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };
        }

        private static GitHubSyncConfig MakeConfig()
        {
            return new GitHubSyncConfig
            {
                Owner = "test-owner",
                Repository = "test-repo",
                Branch = "main",
                Path = "seamless-loop/sync.json",
                Token = "ghp_test_token"
            };
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static Task<HttpResponseMessage> Ok(string json)
        {
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, json));
        }

        private static Task<HttpResponseMessage> Status(HttpStatusCode code)
        {
            return Task.FromResult(new HttpResponseMessage(code));
        }

        private static Task<HttpResponseMessage> StatusWithBody(HttpStatusCode code, string body)
        {
            return Task.FromResult(JsonResponse(code, body));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Download 404 → Exists=false
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Download_404_ReturnsExistsFalse()
        {
            var handler = new FakeHttpMessageHandler(async req =>
            {
                Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
                Assert.That(req.RequestUri.AbsolutePath, Does.Contain("test-owner/test-repo/contents/seamless-loop/sync.json"));
                Assert.That(req.Headers.UserAgent.ToString(), Does.Contain("SeamlessLoopMusic"));
                Assert.That(req.Headers.Authorization?.ToString(), Does.Contain("Bearer ghp_test_token"));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var backend = new GitHubContentsSyncBackend(handler);
            var result = await backend.DownloadAsync(MakeConfig());

            Assert.That(result.Exists, Is.False);
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.Revision, Is.Null);
        }

        // ────────────────────────────────────────────────────────
        //  Test: Download 200 with base64 CR/LF → parsed snapshot
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Download_200_ParsesSnapshotAndSha()
        {
            var snapshot = MakeTestSnapshot();
            var responseJson = BuildGetResponse(SampleSha, snapshot);

            var handler = new FakeHttpMessageHandler(_ =>
                Ok(responseJson));

            var backend = new GitHubContentsSyncBackend(handler);
            var result = await backend.DownloadAsync(MakeConfig());

            Assert.That(result.Exists, Is.True);
            Assert.That(result.Revision, Is.EqualTo(SampleSha));
            Assert.That(result.Snapshot, Is.Not.Null);
            Assert.That(result.Snapshot.SchemaVersion, Is.EqualTo(2));
            Assert.That(result.Snapshot.DeviceId, Is.EqualTo("test-device"));
            Assert.That(result.Snapshot.ExportedAt, Is.EqualTo(1700000000000));
            Assert.That(result.Snapshot.Playlists, Has.Count.EqualTo(1));
            Assert.That(result.Snapshot.Playlists[0].Name, Is.EqualTo("Test PL"));
            Assert.That(result.Snapshot.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(result.Snapshot.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(100));
            Assert.That(result.Snapshot.Ratings, Has.Count.EqualTo(1));
            Assert.That(result.Snapshot.Ratings[0].Rating.RatingValue, Is.EqualTo(4));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Upload with expectedRevision includes sha
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Upload_WithExpectedRevision_SendsShaAndReturnsContentSha()
        {
            var snapshot = MakeTestSnapshot();

            var handler = new FakeHttpMessageHandler(async req =>
            {
                Assert.That(req.Method, Is.EqualTo(HttpMethod.Put));
                var body = await req.Content.ReadAsStringAsync();
                var parsed = JObject.Parse(body);
                Assert.That(parsed["sha"]?.ToString(), Is.EqualTo(SampleSha));
                Assert.That(parsed["branch"]?.ToString(), Is.EqualTo("main"));
                Assert.That(parsed["message"]?.ToString(), Does.Contain("Sync snapshot"));

                // Verify content is valid base64 JSON
                var contentB64 = parsed["content"]?.ToString();
                Assert.That(contentB64, Is.Not.Null);
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(contentB64));
                var deserialized = SyncSnapshotSerializer.Deserialize(decoded);
                Assert.That(deserialized.DeviceId, Is.EqualTo("test-device"));

                var resp = new JObject
                {
                    ["content"] = new JObject { ["sha"] = NewSampleSha },
                    ["commit"] = new JObject { ["sha"] = "commit_sha_ignored" }
                };

                return JsonResponse(HttpStatusCode.Created, resp.ToString(Formatting.None));
            });

            var backend = new GitHubContentsSyncBackend(handler);
            var newRevision = await backend.UploadAsync(MakeConfig(), snapshot, SampleSha);

            Assert.That(newRevision, Is.EqualTo(NewSampleSha));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Upload without expectedRevision
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Upload_WithoutExpectedRevision_OmitsShaFromBody()
        {
            var snapshot = MakeTestSnapshot();

            var handler = new FakeHttpMessageHandler(async req =>
            {
                Assert.That(req.Method, Is.EqualTo(HttpMethod.Put));
                var body = await req.Content.ReadAsStringAsync();
                var parsed = JObject.Parse(body);
                Assert.That(parsed["sha"], Is.Null);

                var resp = new JObject
                {
                    ["content"] = new JObject { ["sha"] = NewSampleSha }
                };
                return JsonResponse(HttpStatusCode.Created, resp.ToString(Formatting.None));
            });

            var backend = new GitHubContentsSyncBackend(handler);
            var newRevision = await backend.UploadAsync(MakeConfig(), snapshot, null);
            Assert.That(newRevision, Is.EqualTo(NewSampleSha));
        }

        [Test]
        public void Upload_CallerCancellation_RethrowsOperationCanceled()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var handler = new FakeHttpMessageHandler(_ =>
                    throw new OperationCanceledException(cancellation.Token));
                var backend = new GitHubContentsSyncBackend(handler);

                var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await backend.UploadAsync(MakeConfig(), MakeTestSnapshot(), null, cancellation.Token));

                Assert.That(exception, Is.Not.TypeOf<SyncBackendException>());
            }
        }

        // ────────────────────────────────────────────────────────
        //  Test: Upload 409 → Conflict
        // ────────────────────────────────────────────────────────

        [Test]
        public void Upload_409_ThrowsConflict()
        {
            var snapshot = MakeTestSnapshot();

            var handler = new FakeHttpMessageHandler(_ =>
                StatusWithBody(HttpStatusCode.Conflict, "{\"message\":\"sha does not match\"}"));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.UploadAsync(MakeConfig(), snapshot, SampleSha));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Conflict));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Upload 422 → Conflict
        // ────────────────────────────────────────────────────────

        [Test]
        public void Upload_422_ThrowsConflict()
        {
            var snapshot = MakeTestSnapshot();

            var handler = new FakeHttpMessageHandler(_ =>
                StatusWithBody((HttpStatusCode)422, "{\"message\":\"invalid request\"}"));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.UploadAsync(MakeConfig(), snapshot, SampleSha));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Conflict));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Download 401 → Unauthorized
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_401_ThrowsUnauthorized()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                Status(HttpStatusCode.Unauthorized));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DownloadAsync(MakeConfig()));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Unauthorized));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Download 403 → Unauthorized
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_403_ThrowsUnauthorized()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                Status(HttpStatusCode.Forbidden));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DownloadAsync(MakeConfig()));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Unauthorized));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Network error → Network code
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_NetworkError_ThrowsNetwork()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                throw new HttpRequestException("Connection refused"));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DownloadAsync(MakeConfig()));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Network));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Invalid JSON response → InvalidRemote
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_InvalidJson_ThrowsInvalidRemote()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                Ok("not json"));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DownloadAsync(MakeConfig()));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.InvalidRemote));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Invalid snapshot schema → InvalidRemote
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_InvalidSnapshotSchema_ThrowsInvalidRemote()
        {
            // Schema version 2
            var contentObj = new JObject
            {
                ["sha"] = SampleSha,
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    "{\"schemaVersion\":2,\"deviceId\":\"d\",\"exportedAt\":1}"))
            };

            var handler = new FakeHttpMessageHandler(_ =>
                Ok(contentObj.ToString(Formatting.None)));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DownloadAsync(MakeConfig()));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.InvalidRemote));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Null/empty content → InvalidRemote
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_MissingContent_ThrowsInvalidRemote()
        {
            var payload = new JObject { ["sha"] = SampleSha }.ToString(Formatting.None);

            var handler = new FakeHttpMessageHandler(_ =>
                Ok(payload));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DownloadAsync(MakeConfig()));

            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.InvalidRemote));
        }

        // ────────────────────────────────────────────────────────
        //  Test: Config validation (null throws)
        // ────────────────────────────────────────────────────────

        [Test]
        public void Download_NullConfig_Throws()
        {
            var backend = new GitHubContentsSyncBackend(new FakeHttpMessageHandler(_ =>
                Status(HttpStatusCode.OK)));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await backend.DownloadAsync(null));
        }

        [Test]
        public void Upload_NullConfig_Throws()
        {
            var backend = new GitHubContentsSyncBackend(new FakeHttpMessageHandler(_ =>
                Status(HttpStatusCode.OK)));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await backend.UploadAsync(null, MakeTestSnapshot(), null));
        }

        [Test]
        public void Upload_NullSnapshot_Throws()
        {
            var backend = new GitHubContentsSyncBackend(new FakeHttpMessageHandler(_ =>
                Status(HttpStatusCode.OK)));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await backend.UploadAsync(MakeConfig(), null, null));
        }

        [Test]
        public void Upload_NonV2Snapshot_RejectsBeforeHttp()
        {
            var requestCount = 0;
            var backend = new GitHubContentsSyncBackend(new FakeHttpMessageHandler(_ =>
            {
                requestCount++;
                return Status(HttpStatusCode.OK);
            }));
            var legacy = new SyncSnapshot { SchemaVersion = 1, DeviceId = "legacy", ExportedAt = 1 };

            Assert.ThrowsAsync<FormatException>(async () =>
                await backend.UploadAsync(MakeConfig(), legacy, null));
            Assert.That(requestCount, Is.EqualTo(0));
        }

        [Test]
        public void Upload_MalformedV2Snapshot_RejectsBeforeHttp()
        {
            var requestCount = 0;
            var backend = new GitHubContentsSyncBackend(new FakeHttpMessageHandler(_ =>
            {
                requestCount++;
                return Status(HttpStatusCode.OK);
            }));
            var malformed = new SyncSnapshot { SchemaVersion = 2, DeviceId = "device", ExportedAt = 1 };

            Assert.ThrowsAsync<FormatException>(async () =>
                await backend.UploadAsync(MakeConfig(), malformed, null));
            Assert.That(requestCount, Is.EqualTo(0));
        }

        // ────────────────────────────────────────────────────────
        //  Delete tests
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Delete_RemoteMissing_ReturnsSuccess()
        {
            var handler = new FakeHttpMessageHandler(async req =>
            {
                // First call is GET to fetch SHA
                Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var backend = new GitHubContentsSyncBackend(handler);
            // Should not throw — 404 on GET means file already gone
            await backend.DeleteAsync(MakeConfig());
        }

        [Test]
        public async Task Delete_RemoteExists_SendsDeleteWithSha()
        {
            int callCount = 0;
            var handler = new FakeHttpMessageHandler(async req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // GET — return snapshot with SHA
                    Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
                    var snapshot = MakeTestSnapshot();
                    var responseJson = BuildGetResponse(SampleSha, snapshot);
                    return JsonResponse(HttpStatusCode.OK, responseJson);
                }

                // DELETE
                Assert.That(req.Method, Is.EqualTo(HttpMethod.Delete));
                var body = await req.Content.ReadAsStringAsync();
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(body);
                Assert.That(parsed["sha"]?.ToString(), Is.EqualTo(SampleSha));
                Assert.That(parsed["branch"]?.ToString(), Is.EqualTo("main"));
                Assert.That(parsed["message"]?.ToString(), Does.Contain("Delete sync snapshot"));

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var backend = new GitHubContentsSyncBackend(handler);
            await backend.DeleteAsync(MakeConfig());
            Assert.That(callCount, Is.EqualTo(2));
        }

        [Test]
        public void Delete_Unauthorized_ThrowsUnauthorized()
        {
            var handler = new FakeHttpMessageHandler(async req =>
            {
                Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DeleteAsync(MakeConfig()));
            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Unauthorized));
        }

        [Test]
        public void Delete_Conflict_ThrowsConflict()
        {
            int callCount = 0;
            var handler = new FakeHttpMessageHandler(async req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // GET returns SHA
                    var snapshot = MakeTestSnapshot();
                    var responseJson = BuildGetResponse(SampleSha, snapshot);
                    return JsonResponse(HttpStatusCode.OK, responseJson);
                }

                // DELETE returns 409
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            });

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DeleteAsync(MakeConfig()));
            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Conflict));
        }

        [Test]
        public void Delete_NetworkError_ThrowsNetwork()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                throw new HttpRequestException("Connection refused"));

            var backend = new GitHubContentsSyncBackend(handler);
            var ex = Assert.ThrowsAsync<SyncBackendException>(async () =>
                await backend.DeleteAsync(MakeConfig()));
            Assert.That(ex.Code, Is.EqualTo(SyncBackendCode.Network));
        }

        [Test]
        public void Delete_NullConfig_Throws()
        {
            var backend = new GitHubContentsSyncBackend(new FakeHttpMessageHandler(_ =>
                Status(HttpStatusCode.OK)));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await backend.DeleteAsync(null));
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Fake HttpMessageHandler for testing
    // ────────────────────────────────────────────────────────────

    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler)
        {
            _handler = asyncHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
