using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class GitHubSyncCoordinatorTests
    {
        private string _dbPath;
        private DatabaseHelper _dbHelper;
        private SQLiteSyncSnapshotStore _store;
        private FakeSyncPreparationService _preparation;
        private const string RemoteSha = "remotesha001";
        private const string UploadedSha = "uploadedsha002";

        private static SyncSnapshot MakeLocalSnapshot()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "local-device",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-local", Name = "Local PL",
                        CreatedAt = 100, ModifiedAt = 100,
                        Items = new List<SyncPlaylistItem>()
                    }
                },
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };
        }

        private static SyncSnapshot MakeRemoteSnapshot()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote-device",
                ExportedAt = 2000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song.mp3", DurationMs = 30000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 5000, LastModified = 500 }
                    }
                },
                Ratings = new List<SyncRatingEntry>(),
                Playlists = new List<SyncPlaylist>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };
        }

        private static GitHubSyncConfig MakeConfig()
        {
            return new GitHubSyncConfig
            {
                Owner = "owner",
                Repository = "repo",
                Branch = "main",
                Path = "sync.json",
                Token = "token"
            };
        }

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"GitHubSyncTest_{Guid.NewGuid()}.db");
            _dbHelper = new DatabaseHelper(_dbPath);
            _dbHelper.InitializeDatabase();
            _store = new SQLiteSyncSnapshotStore(_dbHelper);
            _preparation = new FakeSyncPreparationService(_store);
        }

        [TearDown]
        public void TearDown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Remote missing → initial upload
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_RemoteMissing_UploadsInitial()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot { Exists = false },
                UploadResult = UploadedSha
            };

            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);
            var config = MakeConfig();

            var report = await coordinator.SyncNowAsync(config);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Status, Is.EqualTo("uploaded"));
            Assert.That(fakeBackend.LastUploadedSnapshot, Is.Not.Null);
            Assert.That(fakeBackend.LastExpectedRevision, Is.Null, "Initial upload should not have expectedRevision");

            // Verify state saved
            var savedSha = _dbHelper.GetSetting("Sync.GitHub.LastRemoteSha");
            Assert.That(savedSha, Is.EqualTo(UploadedSha));

            var savedTime = _dbHelper.GetSetting("Sync.GitHub.LastSyncTime");
            Assert.That(savedTime, Is.Not.Null.And.Not.Empty);
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Remote exists → merge → apply → upload
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_RemoteExists_MergesAppliesUploads()
        {
            var remoteSnapshot = MakeRemoteSnapshot();

            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnapshot,
                    Revision = RemoteSha
                },
                UploadResult = UploadedSha
            };

            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);
            var config = MakeConfig();

            var report = await coordinator.SyncNowAsync(config);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Status, Is.EqualTo("applied"));

            // Upload should have used remote SHA as expectedRevision
            Assert.That(fakeBackend.LastExpectedRevision, Is.EqualTo(RemoteSha));

            // State saved
            var savedSha = _dbHelper.GetSetting("Sync.GitHub.LastRemoteSha");
            Assert.That(savedSha, Is.EqualTo(UploadedSha));
        }

        [Test]
        public async Task Coordinator_RemoteV2_UpgradesOutboundToV2()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Revision = RemoteSha,
                    Snapshot = new SyncSnapshot { SchemaVersion = 2, DeviceId = "remote", ExportedAt = 1,
                        PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty() }
                }
            };

            var report = await new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation).SyncNowAsync(MakeConfig());

            Assert.That(report.Success, Is.True);
            Assert.That(fakeBackend.LastUploadedSnapshot.SchemaVersion, Is.EqualTo(2));
        }

        [Test]
        public async Task Coordinator_ConflictThenRedownloadsV2_ContinuesWithV2Upload()
        {
            var localSnapshot = MakeLocalSnapshot();
            await _store.ApplySnapshotAsync(localSnapshot);

            var v1Remote = MakeRemoteSnapshot();
            var downloadCount = 0;
            var uploadCount = 0;

            var fakeBackend = new FakeSyncBackend
            {
                DownloadFunc = (config, ct) =>
                {
                    downloadCount++;
                    return Task.FromResult(downloadCount == 1
                        ? new RemoteSyncSnapshot
                        {
                            Exists = true,
                            Snapshot = v1Remote,
                            Revision = RemoteSha
                        }
                        : new RemoteSyncSnapshot
                        {
                            Exists = true,
                            Snapshot = new SyncSnapshot { SchemaVersion = 2, DeviceId = "remote-v2", ExportedAt = 2,
                                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty() },
                            Revision = "remotev2sha"
                        });
                },
                UploadFunc = (config, snapshot, expectedRevision, ct) =>
                {
                    uploadCount++;
                    if (uploadCount == 1)
                    {
                        throw new SyncBackendException(SyncBackendCode.Conflict, "sha mismatch");
                    }

                    return Task.FromResult(UploadedSha);
                }
            };

            var report = await new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation).SyncNowAsync(MakeConfig(), maxConflictRetries: 2);

            Assert.That(report.Success, Is.True);
            Assert.That(uploadCount, Is.EqualTo(2));
            Assert.That(fakeBackend.LastUploadedSnapshot?.SchemaVersion, Is.EqualTo(2));
            Assert.That(_dbHelper.GetSetting("Sync.GitHub.LastRemoteSha"), Is.EqualTo(UploadedSha));
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Conflict → re-download + retry
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_Conflict_RetriesAndResolves()
        {
            // Remote snapshot exists
            var remoteSnapshot = MakeRemoteSnapshot();

            int callCount = 0;

            var fakeBackend = new FakeSyncBackend
            {
                // First download succeeds
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnapshot,
                    Revision = RemoteSha
                },
                // First upload throws conflict, second succeeds
                UploadFunc = async (config, snapshot, expectedRevision, ct) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        throw new SyncBackendException(SyncBackendCode.Conflict, "sha mismatch");
                    }
                    return UploadedSha;
                },
                // Second download (after conflict) also succeeds
                DownloadFunc = async (config, ct) =>
                {
                    // Return same remote (simulating no further changes)
                    return new RemoteSyncSnapshot
                    {
                        Exists = true,
                        Snapshot = remoteSnapshot,
                        Revision = RemoteSha
                    };
                }
            };

            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);
            var config = MakeConfig();

            var report = await coordinator.SyncNowAsync(config, maxConflictRetries: 3);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Status, Is.EqualTo("conflict_resolved"));
            Assert.That(callCount, Is.EqualTo(2), "Should have retried once");
            Assert.That(_preparation.CaptureCount, Is.EqualTo(4), "Conflict retry must prepare from a fresh local capture.");
        }

        [Test]
        public async Task Coordinator_RemoteDeletedDuringConflict_CapturesFreshLocalBeforeRetry()
        {
            var downloadCount = 0;
            var uploadCount = 0;
            _preparation.CaptureFunc = count => Task.FromResult(new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "capture-" + count,
                ExportedAt = count,
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            });

            var fakeBackend = new FakeSyncBackend
            {
                DownloadFunc = (config, ct) =>
                {
                    downloadCount++;
                    return Task.FromResult(downloadCount == 1
                        ? new RemoteSyncSnapshot { Exists = true, Revision = RemoteSha, Snapshot = new SyncSnapshot { SchemaVersion = 2, DeviceId = "remote", ExportedAt = 1,
                            PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty() } }
                        : new RemoteSyncSnapshot { Exists = false });
                },
                UploadFunc = (config, snapshot, expectedRevision, ct) =>
                {
                    uploadCount++;
                    if (uploadCount == 1) throw new SyncBackendException(SyncBackendCode.Conflict, "sha mismatch");
                    Assert.That(expectedRevision, Is.Null);
                    Assert.That(snapshot.DeviceId, Is.EqualTo("capture-3"));
                    return Task.FromResult(UploadedSha);
                }
            };

            var report = await new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation)
                .SyncNowAsync(MakeConfig(), maxConflictRetries: 1);

            Assert.That(report.Success, Is.True);
            Assert.That(_preparation.CaptureCount, Is.EqualTo(3), "The deleted-remote retry must not reuse the first local snapshot.");
            Assert.That(uploadCount, Is.EqualTo(2));
        }

        [Test]
        public async Task Coordinator_CancelledBeforeInitialUpload_ReturnsCancelledWithoutUpload()
        {
            var uploadCount = 0;
            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot { Exists = false },
                UploadFunc = (config, snapshot, expectedRevision, ct) =>
                {
                    uploadCount++;
                    return Task.FromResult(UploadedSha);
                }
            };
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var report = await new GitHubSyncCoordinator(backend, _dbHelper,
                    new CancellationTolerantPreparationService()).SyncNowAsync(MakeConfig(), ct: cancellation.Token);

                Assert.That(report.Success, Is.False);
                Assert.That(report.Status, Is.EqualTo("cancelled"));
                Assert.That(uploadCount, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task Coordinator_CancelledBeforeNormalUpload_ReturnsCancelledWithoutUpload()
        {
            var uploadCount = 0;
            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Revision = RemoteSha,
                    Snapshot = MakeRemoteSnapshot()
                },
                UploadFunc = (config, snapshot, expectedRevision, ct) =>
                {
                    uploadCount++;
                    return Task.FromResult(UploadedSha);
                }
            };
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var report = await new GitHubSyncCoordinator(backend, _dbHelper,
                    new CancellationTolerantPreparationService()).SyncNowAsync(MakeConfig(), ct: cancellation.Token);

                Assert.That(report.Success, Is.False);
                Assert.That(report.Status, Is.EqualTo("cancelled"));
                Assert.That(uploadCount, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task Coordinator_CancelledBeforeRetryUpload_ReturnsCancelledAfterInitialUpload()
        {
            var uploadCount = 0;
            using (var cancellation = new CancellationTokenSource())
            {
                var backend = new FakeSyncBackend
                {
                    DownloadFunc = (config, ct) => Task.FromResult(new RemoteSyncSnapshot
                    {
                        Exists = true,
                        Revision = RemoteSha,
                        Snapshot = MakeRemoteSnapshot()
                    }),
                    UploadFunc = (config, snapshot, expectedRevision, ct) =>
                    {
                        uploadCount++;
                        if (uploadCount == 1)
                        {
                            cancellation.Cancel();
                            throw new SyncBackendException(SyncBackendCode.Conflict, "sha mismatch");
                        }

                        return Task.FromResult(UploadedSha);
                    }
                };

                var report = await new GitHubSyncCoordinator(backend, _dbHelper,
                    new CancellationTolerantPreparationService()).SyncNowAsync(
                        MakeConfig(), maxConflictRetries: 1, ct: cancellation.Token);

                Assert.That(report.Success, Is.False);
                Assert.That(report.Status, Is.EqualTo("cancelled"));
                Assert.That(uploadCount, Is.EqualTo(1));
            }
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Conflict max retries exceeded
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_ConflictMaxRetries_ReturnsFailure()
        {
            var remoteSnapshot = MakeRemoteSnapshot();

            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnapshot,
                    Revision = RemoteSha
                },
                // Always conflict
                UploadFunc = async (config, snapshot, expectedRevision, ct) =>
                {
                    throw new SyncBackendException(SyncBackendCode.Conflict, "persistent conflict");
                }
            };

            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);
            var config = MakeConfig();

            var report = await coordinator.SyncNowAsync(config, maxConflictRetries: 2);

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status, Is.EqualTo("conflict_max_retries"));
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Unauthorized → failure report
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_Unauthorized_ReturnsFailure()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DownloadFunc = async (config, ct) =>
                {
                    throw new SyncBackendException(SyncBackendCode.Unauthorized, "bad token");
                }
            };

            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);
            var config = MakeConfig();

            var report = await coordinator.SyncNowAsync(config);

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status, Is.EqualTo("unauthorized"));
            Assert.That(report.ErrorMessage, Does.Contain("bad token"));
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Config missing → failure report
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_ConfigMissing_ReturnsFailure()
        {
            var fakeBackend = new FakeSyncBackend();
            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);

            var config = new GitHubSyncConfig
            {
                Owner = "",
                Repository = "repo",
                Token = ""
            };

            var report = await coordinator.SyncNowAsync(config);

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status, Is.EqualTo("not_configured"));
        }

        [Test]
        public async Task Coordinator_NullConfig_ReturnsNotConfigured()
        {
            var fakeBackend = new FakeSyncBackend();
            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);

            var report = await coordinator.SyncNowAsync(null);

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status, Is.EqualTo("not_configured"));
        }

        // ────────────────────────────────────────────────────────
        //  Coordinator: Network error at download → failure
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task Coordinator_NetworkError_ReturnsFailure()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DownloadFunc = async (config, ct) =>
                {
                    throw new SyncBackendException(SyncBackendCode.Network, "connection failed");
                }
            };

            var coordinator = new GitHubSyncCoordinator(fakeBackend, _dbHelper, _preparation);
            var report = await coordinator.SyncNowAsync(MakeConfig());

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status, Is.EqualTo("backend_error"));
            Assert.That(report.ErrorMessage, Does.Contain("connection failed"));
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Fake ISyncBackend for coordinator testing
    // ────────────────────────────────────────────────────────────

    public class FakeSyncBackend : ISyncBackend
    {
        public RemoteSyncSnapshot? DownloadResult { get; set; }
        public string? UploadResult { get; set; }
        public string? LastExpectedRevision { get; set; }
        public SyncSnapshot? LastUploadedSnapshot { get; set; }
        public bool DeleteCalled { get; set; }
        public GitHubSyncConfig? LastDeleteConfig { get; set; }

        public Func<GitHubSyncConfig, CancellationToken, Task<RemoteSyncSnapshot>>? DownloadFunc { get; set; }
        public Func<GitHubSyncConfig, SyncSnapshot, string, CancellationToken, Task<string>>? UploadFunc { get; set; }
        public Func<GitHubSyncConfig, CancellationToken, Task>? DeleteFunc { get; set; }

        public Task<RemoteSyncSnapshot> DownloadAsync(GitHubSyncConfig config, CancellationToken ct = default)
        {
            if (DownloadFunc != null)
                return DownloadFunc(config, ct);

            return Task.FromResult(DownloadResult ?? new RemoteSyncSnapshot { Exists = false });
        }

        public Task<string> UploadAsync(GitHubSyncConfig config, SyncSnapshot snapshot, string expectedRevision, CancellationToken ct = default)
        {
            LastExpectedRevision = expectedRevision;
            LastUploadedSnapshot = snapshot;

            if (UploadFunc != null)
                return UploadFunc(config, snapshot, expectedRevision, ct);

            return Task.FromResult(UploadResult ?? "defaultsha");
        }

        public Task DeleteAsync(GitHubSyncConfig config, CancellationToken ct = default)
        {
            DeleteCalled = true;
            LastDeleteConfig = config;

            if (DeleteFunc != null)
                return DeleteFunc(config, ct);

            return Task.CompletedTask;
        }
    }

    internal sealed class CancellationTolerantPreparationService : IGitHubSyncPreparationService
    {
        private static SyncSnapshot Outbound()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "test-device",
                ExportedAt = 1,
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };
        }

        public Task<SyncSnapshot> CaptureFreshLocalSnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(Outbound());

        public Task<PreparedSyncSnapshot> PrepareNormalSyncAsync(SyncSnapshot remoteSnapshot,
            CancellationToken ct = default)
            => Task.FromResult(new PreparedSyncSnapshot
            {
                Outbound = Outbound(),
                ApplyResult = new SyncApplyResult(),
                Conflicts = new List<SyncMergeConflict>()
            });

        public Task<PreparedSyncSnapshot> PrepareForcePushAsync(SyncSnapshot remoteSnapshot,
            CancellationToken ct = default)
            => Task.FromResult(new PreparedSyncSnapshot
            {
                Outbound = Outbound(),
                ApplyResult = new SyncApplyResult(),
                Conflicts = new List<SyncMergeConflict>()
            });
    }
}
