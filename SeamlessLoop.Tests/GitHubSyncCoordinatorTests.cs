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
        private const string RemoteSha = "remotesha001";
        private const string UploadedSha = "uploadedsha002";

        private static SyncSnapshot MakeLocalSnapshot()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 1,
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
                }
            };
        }

        private static SyncSnapshot MakeRemoteSnapshot()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 1,
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
                Playlists = new List<SyncPlaylist>()
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

            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);
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

            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);
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

            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);
            var config = MakeConfig();

            var report = await coordinator.SyncNowAsync(config, maxConflictRetries: 3);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Status, Is.EqualTo("conflict_resolved"));
            Assert.That(callCount, Is.EqualTo(2), "Should have retried once");
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

            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);
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

            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);
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
            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);

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
            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);

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

            var coordinator = new GitHubSyncCoordinator(_store, fakeBackend, _dbHelper);
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
}
