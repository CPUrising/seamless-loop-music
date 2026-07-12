using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsOutboxTests
    {
        [Test]
        public async Task LoadState_ValidCurrentEnvelopeLoadsSettlementEvents()
        {
            var path = NewPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                await outbox.SaveSettlementEventsAsync(new[] { Settlement("event") });

                var state = outbox.LoadState();
                Assert.That(state.Version, Is.EqualTo(2));
                Assert.That(state.SettlementEvents.Select(x => x.SettlementEventId), Is.EqualTo(new[] { "event" }));
            }
            finally { DeleteDirectory(path); }
        }

        [TestCase("{\"Version\":1,\"SettlementEvents\":[]}")]
        [TestCase("[{\"SettlementEventId\":\"legacy\"}]")]
        [TestCase("{\"Version\":2,\"SettlementEvents\":[],\"LegacySegments\":[],\"IsLegacyFormat\":true}")]
        [TestCase("{\"Version\":2,\"SettlementEvents\":[],\"Unknown\":true}")]
        public void LoadState_InvalidEnvelopeIsRejectedAndReplacedWithEmptyCurrentEnvelope(string json)
        {
            var path = NewPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);

                var state = new PlaybackStatisticsOutbox(path).LoadState();

                Assert.That(state.Version, Is.EqualTo(2));
                Assert.That(state.SettlementEvents, Is.Empty);
                Assert.That(File.Exists(path), Is.True);
                var written = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(path));
                Assert.That(written.Keys, Is.EquivalentTo(new[] { "Version", "SettlementEvents" }));
            }
            finally { DeleteDirectory(path); }
        }

        [Test]
        public void LoadState_InvalidPrimaryRestoresValidBackup()
        {
            var path = NewPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                outbox.SaveSettlementEvents(new[] { Settlement("backup") });
                File.Move(path, path + ".bak");
                File.WriteAllText(path, "not-json");

                var state = outbox.LoadState();

                Assert.That(state.SettlementEvents.Single().SettlementEventId, Is.EqualTo("backup"));
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(path + ".bak"), Is.False);
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), "*.corrupt"), Has.Length.EqualTo(1));
            }
            finally { DeleteDirectory(path); }
        }

        [Test]
        public void LoadState_BothInvalidIsolatesBothAndWritesEmptyCurrentEnvelope()
        {
            var path = NewPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "bad-primary");
                File.WriteAllText(path + ".bak", "bad-backup");

                var state = new PlaybackStatisticsOutbox(path).LoadState();

                Assert.That(state.SettlementEvents, Is.Empty);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(path + ".bak"), Is.False);
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), "*.corrupt"), Has.Length.EqualTo(2));
            }
            finally { DeleteDirectory(path); }
        }

        [Test]
        public async Task SaveSettlementEvents_AtomicallyReplacesAndReloads()
        {
            var path = NewPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                await outbox.SaveSettlementEventsAsync(new[] { Settlement("old") });
                await outbox.SaveSettlementEventsAsync(new[] { Settlement("new") });

                Assert.That(outbox.LoadState().SettlementEvents.Single().SettlementEventId, Is.EqualTo("new"));
                Assert.That(File.Exists(path + ".bak"), Is.False);
            }
            finally { DeleteDirectory(path); }
        }

        [Test]
        public void SaveSettlementEvents_RejectsNullAndInvalidSettlements()
        {
            var path = NewPath();
            try
            {
                var outbox = new PlaybackStatisticsOutbox(path);
                Assert.Throws<ArgumentNullException>(() => outbox.SaveSettlementEvents(null));
                Assert.Throws<ArgumentException>(() => outbox.SaveSettlementEvents(new[] { new PlaybackStatisticsSettlement() }));
            }
            finally { DeleteDirectory(path); }
        }

        private static PlaybackStatisticsSettlement Settlement(string id)
        {
            return new PlaybackStatisticsSettlement
            {
                SettlementEventId = id,
                FileName = "song.mp3",
                NormalizedFileName = "song.mp3",
                TrackDurationMs = 1000,
                DeviceId = "device",
                Generation = 0,
                StartedAtUtcMs = 1,
                DurationMs = 100,
                AppliedAtUtcMs = 2,
                SourceKind = "test"
            };
        }

        private static string NewPath()
        {
            return Path.Combine(Path.GetTempPath(), "PlaybackOutbox_" + Guid.NewGuid().ToString("N"), "PlaybackStatistics.pending.json");
        }

        private static void DeleteDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
