using System.Globalization;
using System.Linq;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.UI.ViewModels.Settings;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsSourceDeviceRowTests
    {
        private CultureInfo _originalCulture;

        [SetUp]
        public void SetUp()
        {
            _originalCulture = LocalizationService.Instance.CurrentCulture;
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationService.Instance.CurrentCulture = _originalCulture;
        }

        [Test]
        public void FallbackDisplayName_UsesLocalizedPlatformAndSuffix()
        {
            LocalizationService.Instance.CurrentCulture = new CultureInfo("zh-CN");

            var row = new PlaybackStatisticsSourceDeviceRow(new PlaybackStatisticsSourceDevice
            {
                DeviceId = "device-ab12",
                Platform = "windows",
                EffectiveTotalListenMs = 0
            });

            Assert.That(row.DisplayName, Is.EqualTo("Windows 设备 ab12"));
            Assert.That(row.SecondaryText, Is.EqualTo("Windows · ab12"));
        }

        [Test]
        public void LocalDevice_CannotBeSelected()
        {
            var row = new PlaybackStatisticsSourceDeviceRow(new PlaybackStatisticsSourceDevice
            {
                DeviceId = "local-0001",
                Platform = "android",
                IsLocalDevice = true,
                EffectiveTotalListenMs = 0
            });

            row.IsSelected = true;

            Assert.That(row.CanSelect, Is.False);
            Assert.That(row.IsSelected, Is.False);
        }

        [Test]
        public void DurationText_UsesExistingPlaybackStatisticsFormatting()
        {
            LocalizationService.Instance.CurrentCulture = new CultureInfo("en-US");

            var row = new PlaybackStatisticsSourceDeviceRow(new PlaybackStatisticsSourceDevice
            {
                DeviceId = "remote-1000",
                Platform = "windows",
                EffectiveTotalListenMs = 3661000
            });

            Assert.That(row.EffectiveListeningDurationText, Is.EqualTo("1h 1m"));
        }

        [Test]
        public void FullyDeletedRemoteSources_AreCollapsedIntoOneSummaryRow()
        {
            LocalizationService.Instance.CurrentCulture = new CultureInfo("en-US");
            var sources = new[]
            {
                Source("old laptop", "old-laptop-001", "windows", false, 0),
                Source("old phone", "old-phone-002", "android", false, 0),
                Source("old tablet", "old-tablet-003", "linux", false, 0)
            };

            var rows = PlaybackStatisticsSourceDeviceRow.CreateRows(sources);

            Assert.That(rows, Has.Count.EqualTo(1));
            var summary = rows.Single();
            Assert.That(summary.IsDeletedDataSummary, Is.True);
            Assert.That(summary.DeletedSourceCount, Is.EqualTo(3));
            Assert.That(summary.CanSelect, Is.False);
            Assert.That(summary.CanRename, Is.False);
            summary.IsSelected = true;
            Assert.That(summary.IsSelected, Is.False);
            Assert.That(summary.DeviceId, Is.Empty);
            Assert.That(summary.EffectiveListeningDurationText, Is.Empty);
            foreach (var summaryText in new[] { summary.DisplayName, summary.SecondaryText })
            {
                foreach (var oldSourceValue in new[]
                {
                    "old laptop", "old phone", "old tablet",
                    "old-laptop-001", "old-phone-002", "old-tablet-003",
                    "windows", "android", "linux"
                })
                {
                    Assert.That(summaryText, Does.Not.Contain(oldSourceValue));
                }
            }
        }

        [Test]
        public void LocalDeviceWithNoActiveGenerations_RemainsANormalRow()
        {
            var rows = PlaybackStatisticsSourceDeviceRow.CreateRows(new[]
            {
                Source("this computer", "local-001", "windows", true, 0)
            });

            var row = rows.Single();

            Assert.That(row.IsDeletedDataSummary, Is.False);
            Assert.That(row.IsLocalDevice, Is.True);
            Assert.That(row.DeviceId, Is.EqualTo("local-001"));
            Assert.That(row.CanRename, Is.True);
        }

        [Test]
        public void ActiveRowsAreOrderedAfterLocalDeviceAndBeforeDeletedSummary()
        {
            var rows = PlaybackStatisticsSourceDeviceRow.CreateRows(new[]
            {
                Source("Zulu", "remote-z", "windows", false, 1),
                Source("This computer", "local-001", "windows", true, 0),
                Source("Alpha", "remote-a", "android", false, 2),
                Source("Deleted source", "deleted-001", "linux", false, 0)
            });

            Assert.That(rows, Has.Count.EqualTo(4));
            Assert.That(rows[0].DeviceId, Is.EqualTo("local-001"));
            Assert.That(rows[1].DeviceId, Is.EqualTo("remote-a"));
            Assert.That(rows[2].DeviceId, Is.EqualTo("remote-z"));
            Assert.That(rows[3].IsDeletedDataSummary, Is.True);
            Assert.That(rows[3].DeletedSourceCount, Is.EqualTo(1));
        }

        [Test]
        public void RefreshLocalizedText_UpdatesDeletedSummaryBetweenLanguages()
        {
            LocalizationService.Instance.CurrentCulture = new CultureInfo("en-US");
            var summary = PlaybackStatisticsSourceDeviceRow.CreateRows(new[]
            {
                Source("old source", "old-001", "windows", false, 0),
                Source("another old source", "old-002", "android", false, 0)
            }).Single();
            var englishDisplayName = summary.DisplayName;
            var englishSecondaryText = summary.SecondaryText;
            Assert.That(englishDisplayName, Is.EqualTo("Deleted playback history"));
            Assert.That(englishSecondaryText, Is.EqualTo("Deleted history from 2 sources"));

            LocalizationService.Instance.CurrentCulture = new CultureInfo("zh-CN");
            summary.RefreshLocalizedText();

            Assert.That(summary.DisplayName, Is.EqualTo("已删除的播放历史"));
            Assert.That(summary.SecondaryText, Is.EqualTo("来自 2 个来源"));
            Assert.That(summary.DisplayName, Is.Not.EqualTo(englishDisplayName));
            Assert.That(summary.SecondaryText, Is.Not.EqualTo(englishSecondaryText));
        }

        private static PlaybackStatisticsSourceDevice Source(string displayName, string deviceId, string platform, bool isLocalDevice, int activeGenerationCount)
        {
            return new PlaybackStatisticsSourceDevice
            {
                DisplayName = displayName,
                DeviceId = deviceId,
                Platform = platform,
                IsLocalDevice = isLocalDevice,
                KnownActiveGenerationCount = activeGenerationCount
            };
        }
    }
}
