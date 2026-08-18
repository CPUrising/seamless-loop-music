using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Services.Update;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class AppUpdateServiceTests
    {
        private string _dbPath;
        private DatabaseHelper _dbHelper;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"UpdateTest_{Guid.NewGuid()}.db");
            _dbHelper = new DatabaseHelper(_dbPath);
            _dbHelper.InitializeDatabase();
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
        //  SemanticVersion comparison
        // ────────────────────────────────────────────────────────

        [Test]
        public void SemanticVersion_ParsesTagAndComparesNumericParts()
        {
            var current = SemanticVersion.FromString("1.10.4");
            var latest = SemanticVersion.FromTag("v1.11.0");

            Assert.That(latest, Is.Not.Null);
            Assert.That(SemanticVersion.IsNewer(latest, current), Is.True);
        }

        [Test]
        public void SemanticVersion_EqualVersion_IsNotNewer()
        {
            var current = SemanticVersion.FromString("1.11.0");
            var latest = SemanticVersion.FromTag("v1.11.0");

            Assert.That(SemanticVersion.IsNewer(latest, current), Is.False);
        }

        [Test]
        public void SemanticVersion_OlderTag_IsNotNewer()
        {
            var current = SemanticVersion.FromString("1.11.0");
            var latest = SemanticVersion.FromTag("v1.10.4");

            Assert.That(SemanticVersion.IsNewer(latest, current), Is.False);
        }

        [Test]
        public void SemanticVersion_Prerelease_IsOlderThanStable()
        {
            var stable = SemanticVersion.FromString("1.9.0");
            var prerelease = SemanticVersion.FromTag("v1.9.0-alpha");

            Assert.That(SemanticVersion.IsNewer(prerelease, stable), Is.False);
            Assert.That(SemanticVersion.IsNewer(stable, prerelease), Is.True);
        }

        [Test]
        public void SemanticVersion_InvalidTag_ReturnsNull()
        {
            Assert.That(SemanticVersion.FromTag("not-a-version"), Is.Null);
            Assert.That(SemanticVersion.FromTag(null), Is.Null);
            Assert.That(SemanticVersion.IsNewer(null, SemanticVersion.FromString("1.0.0")), Is.False);
        }

        // ────────────────────────────────────────────────────────
        //  CheckForUpdateAsync
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task CheckForUpdateAsync_DetectsNewerReleaseAndFindsInstallerAsset()
        {
            var handler = new FakeHttpMessageHandler(req =>
            {
                Assert.That(req.RequestUri.AbsolutePath, Does.EndWith("/releases/latest"));
                return Task.FromResult(JsonResponse(new ReleasePayloadJson(
                    "v9.9.9",
                    new[] { ("SeamlessLoopMusic-Portable-9.9.9.zip", "https://example.com/portable.zip"), ("SeamlessLoopMusic-Setup-9.9.9.exe", "https://example.com/setup.exe") }
                )));
            });

            var service = new AppUpdateService(_dbHelper, new HttpClient(handler));
            var result = await service.CheckForUpdateAsync();

            Assert.That(result.IsError, Is.False);
            Assert.That(result.UpdateAvailable, Is.True);
            Assert.That(result.LatestVersion, Is.EqualTo("9.9.9"));
            Assert.That(result.InstallerAssetName, Is.EqualTo("SeamlessLoopMusic-Setup-9.9.9.exe"));
            Assert.That(result.InstallerDownloadUrl, Is.EqualTo("https://example.com/setup.exe"));
        }

        [Test]
        public async Task CheckForUpdateAsync_SameVersion_ReportsNoUpdate()
        {
            var currentVersion = new AppUpdateService(_dbHelper, new HttpClient(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage())))).GetCurrentVersion();
            var service = new AppUpdateService(_dbHelper, new HttpClient(new FakeHttpMessageHandler(_ =>
                Task.FromResult(JsonResponse(new ReleasePayloadJson("v" + currentVersion, Array.Empty<(string, string)>()))))));

            var result = await service.CheckForUpdateAsync();

            Assert.That(result.UpdateAvailable, Is.False);
        }

        [Test]
        public async Task CheckForUpdateAsync_NoInstallerAsset_ReportsUpdateButNoDownloadUrl()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                Task.FromResult(JsonResponse(new ReleasePayloadJson(
                    "v9.9.9",
                    new[] { ("SeamlessLoopMusic-Portable-9.9.9.zip", "https://example.com/portable.zip") }
                ))));

            var service = new AppUpdateService(_dbHelper, new HttpClient(handler));
            var result = await service.CheckForUpdateAsync();

            Assert.That(result.UpdateAvailable, Is.True);
            Assert.That(result.InstallerDownloadUrl, Is.Null);
        }

        [Test]
        public async Task CheckForUpdateAsync_NotFound_ReturnsError()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var service = new AppUpdateService(_dbHelper, new HttpClient(handler));
            var result = await service.CheckForUpdateAsync();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.UpdateAvailable, Is.False);
        }

        [Test]
        public async Task CheckForUpdateAsync_NetworkFailure_ReturnsError()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                throw new HttpRequestException("boom"));

            var service = new AppUpdateService(_dbHelper, new HttpClient(handler));
            var result = await service.CheckForUpdateAsync();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.UpdateAvailable, Is.False);
        }

        // ────────────────────────────────────────────────────────
        //  DownloadInstallerAsync
        // ────────────────────────────────────────────────────────

        [Test]
        public async Task DownloadInstallerAsync_WritesFileToTempUpdateFolder()
        {
            var payload = Encoding.UTF8.GetBytes("MZ fake installer payload");
            var handler = new FakeHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                }));

            var service = new AppUpdateService(_dbHelper, new HttpClient(handler));
            var check = new UpdateCheckResult
            {
                LatestVersion = "1.11.0",
                InstallerAssetName = "SeamlessLoopMusic-Setup-1.11.0.exe",
                InstallerDownloadUrl = "https://example.com/setup.exe"
            };

            var localPath = await service.DownloadInstallerAsync(check);

            Assert.That(File.Exists(localPath), Is.True);
            Assert.That(File.ReadAllBytes(localPath), Is.EqualTo(payload));

            try { File.Delete(localPath); } catch { }
        }

        // ────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────

        private static HttpResponseMessage JsonResponse(object payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            };
        }

        private sealed class ReleasePayloadJson
        {
            public string tag_name { get; }
            public string name { get; }
            public string body { get; }
            public AssetJson[] assets { get; }

            public ReleasePayloadJson(string tagName, (string Name, string Url)[] assets)
            {
                tag_name = tagName;
                name = $"Seamless Loop Music {tagName}";
                body = "Release notes for " + tagName;
                var list = new System.Collections.Generic.List<AssetJson>();
                foreach (var a in assets)
                {
                    list.Add(new AssetJson { name = a.Name, browser_download_url = a.Url });
                }
                this.assets = list.ToArray();
            }
        }

        private sealed class AssetJson
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
        }
    }
}
