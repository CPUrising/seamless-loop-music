using System;
using System.IO;
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
    public class GitHubSyncServiceTests
    {
        private string _dbPath;
        private DatabaseHelper _dbHelper;
        private SQLiteSyncSnapshotStore _store;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"GitHubServiceTest_{Guid.NewGuid()}.db");
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
        //  GetConfig: default values
        // ────────────────────────────────────────────────────────

        [Test]
        public void GetConfig_ReturnsDefaults_WhenNothingSaved()
        {
            var backend = new FakeSyncBackend();
            var service = new GitHubSyncService(_dbHelper, _store, backend);

            var config = service.GetConfig();

            Assert.That(config.Owner, Is.EqualTo(""));
            Assert.That(config.Repository, Is.EqualTo(""));
            Assert.That(config.Branch, Is.EqualTo("main"));
            Assert.That(config.Path, Is.EqualTo("seamless-loop/sync.json"));
            Assert.That(config.Token, Is.EqualTo(""));
            Assert.That(config.IsConfigured, Is.False);
        }

        // ────────────────────────────────────────────────────────
        //  SaveConfig / GetConfig round-trip
        // ────────────────────────────────────────────────────────

        [Test]
        public void SaveConfig_RoundTrips()
        {
            var backend = new FakeSyncBackend();
            var service = new GitHubSyncService(_dbHelper, _store, backend);

            var input = new GitHubSyncConfig
            {
                Owner = "my-org",
                Repository = "my-repo",
                Branch = "develop",
                Path = "custom/path/sync.json",
                Token = "ghp_secret"
            };

            service.SaveConfig(input);
            var output = service.GetConfig();

            Assert.That(output.Owner, Is.EqualTo("my-org"));
            Assert.That(output.Repository, Is.EqualTo("my-repo"));
            Assert.That(output.Branch, Is.EqualTo("develop"));
            Assert.That(output.Path, Is.EqualTo("custom/path/sync.json"));
            Assert.That(output.Token, Is.EqualTo("ghp_secret"));
            Assert.That(output.IsConfigured, Is.True);
        }

        // ────────────────────────────────────────────────────────
        //  SaveConfig with null values
        // ────────────────────────────────────────────────────────

        [Test]
        public void SaveConfig_NullFields_FallsBackToDefaults()
        {
            var backend = new FakeSyncBackend();
            var service = new GitHubSyncService(_dbHelper, _store, backend);

            service.SaveConfig(new GitHubSyncConfig
            {
                Owner = "o",
                Repository = "r",
                Token = "t",
                Branch = null,
                Path = null
            });

            var config = service.GetConfig();
            Assert.That(config.Branch, Is.EqualTo("main"));
            Assert.That(config.Path, Is.EqualTo("seamless-loop/sync.json"));
        }

        // ────────────────────────────────────────────────────────
        //  GetLastSyncTimeDisplay
        // ────────────────────────────────────────────────────────

        [Test]
        public void GetLastSyncTimeDisplay_ReturnsNever_WhenNeverSynced()
        {
            var backend = new FakeSyncBackend();
            var service = new GitHubSyncService(_dbHelper, _store, backend);

            Assert.That(service.GetLastSyncTimeDisplay(), Is.EqualTo("Never"));
        }

        [Test]
        public void GetLastSyncTimeDisplay_ReturnsFormatted_WhenSynced()
        {
            var backend = new FakeSyncBackend();
            var service = new GitHubSyncService(_dbHelper, _store, backend);

            var epochMs = new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds();
            _dbHelper.SetSetting("Sync.GitHub.LastSyncTime", epochMs.ToString());

            var display = service.GetLastSyncTimeDisplay();
            // Should contain 2026-06-15 and time part in local zone
            Assert.That(display, Does.Contain("2026-06-15"));
            Assert.That(display, Does.Contain(":")); // has time component
        }

        // ────────────────────────────────────────────────────────
        //  SyncNowAsync: delegates to coordinator
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task SyncNowAsync_DelegatesToCoordinator()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot { Exists = false },
                UploadResult = "some_sha"
            };

            var service = new GitHubSyncService(_dbHelper, _store, fakeBackend);

            // Save a valid config
            service.SaveConfig(new GitHubSyncConfig
            {
                Owner = "o",
                Repository = "r",
                Token = "t"
            });

            var report = await service.SyncNowAsync();

            Assert.That(report.Success, Is.True);
            Assert.That(report.Status, Is.EqualTo("uploaded"));
        }

        [Test]
        public async Task SyncNowAsync_NotConfigured_ReturnsFailure()
        {
            var fakeBackend = new FakeSyncBackend();
            var service = new GitHubSyncService(_dbHelper, _store, fakeBackend);

            var report = await service.SyncNowAsync();

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status, Is.EqualTo("not_configured"));
        }
    }
}
