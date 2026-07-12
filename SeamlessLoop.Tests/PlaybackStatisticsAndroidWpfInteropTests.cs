using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public sealed class PlaybackStatisticsAndroidWpfInteropTests
    {
        private const string GoldenPath = @"E:\codeproject\SeamlessLoopMobileNative\app\src\test\resources\sync\playback_stats_v2_android_golden.json";
        private const string CollisionPath = @"E:\codeproject\SeamlessLoopMobileNative\app\src\test\resources\sync\playback_stats_v2_tombstone_collision.json";
        private const string WpfFixturePath = @"E:\codeproject\dai\SeamlessLoop.Tests\Fixtures\Sync\playback_stats_v2_wpf_canonical.json";
        private const string ReportPath = @"E:\codeproject\dai\SeamlessLoop.Tests\Fixtures\Sync\playback_stats_v2_android_wpf_diff.md";
        private const string WpfDeviceId = "desktop-wpf-1";

        [Test]
        public void AndroidFixtures_MergeBothDirections_MatchCommittedWpfArtifacts()
        {
            var goldenJson = ReadRequiredAndroidFixture(GoldenPath);
            var collisionJson = ReadRequiredAndroidFixture(CollisionPath);
            var golden = SyncSnapshotSerializer.Deserialize(goldenJson);
            var collision = SyncSnapshotSerializer.Deserialize(collisionJson);

            var forward = SyncMergeEngine.Merge(golden, collision).Merged;
            var reverse = SyncMergeEngine.Merge(collision, golden).Merged;
            var forwardStatistics = PlaybackStatisticsToken(forward);
            var reverseStatistics = PlaybackStatisticsToken(reverse);
            var statisticsEqual = JToken.DeepEquals(forwardStatistics, reverseStatistics);
            var topLevelDifferences = TopLevelDifferences(forward, reverse);

            Assert.That(statisticsEqual, Is.True, "Playback statistics differ between golden+collision and collision+golden.");
            Assert.That(topLevelDifferences.Keys, Is.EquivalentTo(new[] { "deviceId" }), "Only documented base-local provenance may differ.");
            Assert.That(forward.ExportedAt, Is.EqualTo(reverse.ExportedAt));

            NormalizeWpfProvenance(forward, golden, collision);
            NormalizeWpfProvenance(reverse, golden, collision);
            var canonicalJson = SyncSnapshotSerializer.Serialize(forward);
            var reverseCanonicalJson = SyncSnapshotSerializer.Serialize(reverse);
            Assert.That(canonicalJson, Is.EqualTo(reverseCanonicalJson), "Normalized canonical snapshots differ.");

            var output = JObject.Parse(canonicalJson);
            var assertions = EvaluateFixtureAssertions(output);
            var roundTripJson = SyncSnapshotSerializer.Serialize(SyncSnapshotSerializer.Deserialize(canonicalJson));
            var roundTripStable = string.Equals(canonicalJson, roundTripJson, StringComparison.Ordinal);

            AssertFixtureAssertions(assertions);
            Assert.That(roundTripStable, Is.True, "Generated WPF fixture is not byte-stable after strict round-trip.");

            var fixtureJson = ReadRequiredFixture(WpfFixturePath);
            Assert.That(fixtureJson, Is.EqualTo(canonicalJson), "Committed WPF fixture is stale or tampered.");

            var expectedReport = BuildReport(
                goldenJson,
                collisionJson,
                statisticsEqual,
                topLevelDifferences,
                assertions,
                roundTripStable,
                canonicalJson);
            Assert.That(ReadRequiredFixture(ReportPath), Is.EqualTo(expectedReport), "Committed interop report is stale or tampered.");
        }

        [Test]
        public void WpfCanonicalFixture_StrictRoundTripIsByteStable()
        {
            Assert.That(File.Exists(WpfFixturePath), Is.True, "Missing generated WPF fixture: " + WpfFixturePath);
            var fixtureJson = File.ReadAllText(WpfFixturePath);
            var roundTripJson = SyncSnapshotSerializer.Serialize(SyncSnapshotSerializer.Deserialize(fixtureJson));
            Assert.That(roundTripJson, Is.EqualTo(fixtureJson), "WPF fixture changed after strict deserialize/canonical serialize.");
        }

        private static string ReadRequiredAndroidFixture(string path)
        {
            Assert.That(File.Exists(path), Is.True, "Required Android interop fixture is missing: " + path);
            return File.ReadAllText(path);
        }

        private static string ReadRequiredFixture(string path)
        {
            Assert.That(File.Exists(path), Is.True, "Required committed interop artifact is missing: " + path);
            return File.ReadAllText(path);
        }

        private static void NormalizeWpfProvenance(SyncSnapshot snapshot, SyncSnapshot golden, SyncSnapshot collision)
        {
            snapshot.DeviceId = WpfDeviceId;
            snapshot.ExportedAt = Math.Max(golden.ExportedAt, collision.ExportedAt);
        }

        private static JObject SnapshotToken(SyncSnapshot snapshot)
        {
            return JObject.Parse(SyncSnapshotSerializer.Serialize(snapshot));
        }

        private static JToken PlaybackStatisticsToken(SyncSnapshot snapshot)
        {
            return SnapshotToken(snapshot)["playbackStatistics"];
        }

        private static Dictionary<string, string> TopLevelDifferences(SyncSnapshot left, SyncSnapshot right)
        {
            var leftToken = SnapshotToken(left);
            var rightToken = SnapshotToken(right);
            var differences = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in leftToken.Properties().Where(x => x.Name != "playbackStatistics"))
            {
                var rightValue = rightToken[property.Name];
                if (!JToken.DeepEquals(property.Value, rightValue))
                    differences[property.Name] = property.Value.ToString(Formatting.None) + " vs " + (rightValue?.ToString(Formatting.None) ?? "<missing>");
            }
            return differences;
        }

        private static FixtureAssertions EvaluateFixtureAssertions(JObject output)
        {
            var statistics = output["playbackStatistics"] as JObject;
            var contributions = statistics?["songs"]?
                .Children<JObject>()
                .SelectMany(song => (song["contributions"] as JArray)?.Children<JObject>() ?? Enumerable.Empty<JObject>())
                .ToList() ?? new List<JObject>();
            var unresolved = statistics?["songs"]?
                .Children<JObject>()
                .SingleOrDefault(song => (string)song["song"]?["normalizedFileName"] == "unresolved mix.flac");
            var unresolvedIdentity = unresolved?["song"] as JObject;
            var unresolvedContribution = unresolved?["contributions"]?
                .Children<JObject>()
                .SingleOrDefault(contribution => (string)contribution["deviceId"] == WpfDeviceId && (long?)contribution["generation"] == 0);
            var propertyNames = new HashSet<string>(output.DescendantsAndSelf().OfType<JProperty>().Select(x => x.Name), StringComparer.Ordinal);

            return new FixtureAssertions
            {
                Schema2 = (int?)output["schemaVersion"] == 2,
                HasPlaybackStatistics = statistics != null,
                UsesSourceLocalBasis = (string)statistics?["dateBucketBasis"] == "sourceLocal",
                UsesDatedListenMs = propertyNames.Contains("datedListenMs"),
                HasNoPlaybackStatsAlias = !propertyNames.Contains("playbackStats"),
                HasNoDailyListenMsAlias = !propertyNames.Contains("dailyListenMs"),
                HasNoSourceLocalAlias = !propertyNames.Contains("source-local"),
                AndroidGeneration1Suppressed = contributions.All(x => !((string)x["deviceId"] == "android-pixel-8" && (long?)x["generation"] == 1)),
                AndroidGeneration2Survives = contributions.Any(x => (string)x["deviceId"] == "android-pixel-8" && (long?)x["generation"] == 2
                    && (long?)x["datedListenMs"]?["2026-07-10"] == 60000
                    && (long?)x["datedListenMs"]?["2026-07-11"] == 30000),
                TombstoneGeneration1Survives = statistics?["tombstones"]?.Children<JObject>().Any(x => (string)x["deviceId"] == "android-pixel-8" && (long?)x["generation"] == 1) == true,
                UnresolvedIdentitySurvives = (string)unresolvedIdentity?["fileName"] == "Unresolved Mix.FLAC"
                    && (string)unresolvedIdentity?["normalizedFileName"] == "unresolved mix.flac"
                    && unresolvedIdentity?["totalSamples"] == null
                    && unresolvedIdentity?["contentHash"] == null,
                UnresolvedContributionSurvives = unresolvedContribution != null
                    && (long?)unresolvedContribution["undatedListenMs"] == 9876
                    && unresolvedContribution["datedListenMs"] is JObject dated && !dated.Properties().Any(),
                OptionalMetadataAbsentOnWire = unresolvedIdentity?["totalSamples"] == null && unresolvedIdentity?["contentHash"] == null
            };
        }

        private static void AssertFixtureAssertions(FixtureAssertions assertions)
        {
            Assert.That(assertions.Schema2, Is.True);
            Assert.That(assertions.HasPlaybackStatistics, Is.True);
            Assert.That(assertions.UsesSourceLocalBasis, Is.True);
            Assert.That(assertions.UsesDatedListenMs, Is.True);
            Assert.That(assertions.HasNoPlaybackStatsAlias, Is.True);
            Assert.That(assertions.HasNoDailyListenMsAlias, Is.True);
            Assert.That(assertions.HasNoSourceLocalAlias, Is.True);
            Assert.That(assertions.AndroidGeneration1Suppressed, Is.True);
            Assert.That(assertions.AndroidGeneration2Survives, Is.True);
            Assert.That(assertions.TombstoneGeneration1Survives, Is.True);
            Assert.That(assertions.UnresolvedIdentitySurvives, Is.True);
            Assert.That(assertions.UnresolvedContributionSurvives, Is.True);
            Assert.That(assertions.OptionalMetadataAbsentOnWire, Is.True);
        }

        private static string BuildReport(
            string goldenJson,
            string collisionJson,
            bool statisticsEqual,
            Dictionary<string, string> topLevelDifferences,
            FixtureAssertions assertions,
            bool roundTripStable,
            string canonicalJson)
        {
            var provenance = topLevelDifferences.Count == 0
                ? "none"
                : string.Join("; ", topLevelDifferences.Select(x => x.Key + ": " + x.Value));
            return string.Join("\n", new[]
            {
                "# Playback Statistics Android/WPF Interop",
                "",
                "## Inputs",
                "- Golden: " + GoldenPath,
                "  - SHA-256: " + Sha256(goldenJson),
                "- Tombstone collision: " + CollisionPath,
                "  - SHA-256: " + Sha256(collisionJson),
                "",
                "## Merge",
                "- Production merge order: golden + collision and collision + golden",
                "- Canonical playbackStatistics equality: " + Result(statisticsEqual),
                "- Top-level provenance differences before normalization: " + provenance,
                "- WPF provenance: deviceId=`" + WpfDeviceId + "`, exportedAt=max(input exportedAt)",
                "",
                "## Assertions",
                "- Wire: schema2=`" + Result(assertions.Schema2) + "`, playbackStatistics=`" + Result(assertions.HasPlaybackStatistics) + "`, sourceLocal=`" + Result(assertions.UsesSourceLocalBasis) + "`, datedListenMs=`" + Result(assertions.UsesDatedListenMs) + "`",
                "- Forbidden aliases absent: playbackStats=`" + Result(assertions.HasNoPlaybackStatsAlias) + "`, dailyListenMs=`" + Result(assertions.HasNoDailyListenMsAlias) + "`, source-local=`" + Result(assertions.HasNoSourceLocalAlias) + "`",
                "- android-pixel-8 generation 1 contribution suppressed: " + Result(assertions.AndroidGeneration1Suppressed),
                "- android-pixel-8 generation 2 dated buckets survive: " + Result(assertions.AndroidGeneration2Survives),
                "- android-pixel-8 generation 1 tombstone survives: " + Result(assertions.TombstoneGeneration1Survives),
                "- Unresolved Mix.FLAC identity and desktop generation 0 contribution survive: " + Result(assertions.UnresolvedIdentitySurvives && assertions.UnresolvedContributionSurvives),
                "- Unresolved optional metadata remains absent: " + Result(assertions.OptionalMetadataAbsentOnWire),
                "",
                "## Round Trip",
                "- WPF fixture strict deserialize + canonical serialize byte identity: " + Result(roundTripStable),
                "- WPF fixture SHA-256: " + Sha256(canonicalJson),
                ""
            });
        }

        private static string Result(bool value) => value ? "PASS" : "FAIL";

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        private sealed class FixtureAssertions
        {
            public bool Schema2 { get; set; }
            public bool HasPlaybackStatistics { get; set; }
            public bool UsesSourceLocalBasis { get; set; }
            public bool UsesDatedListenMs { get; set; }
            public bool HasNoPlaybackStatsAlias { get; set; }
            public bool HasNoDailyListenMsAlias { get; set; }
            public bool HasNoSourceLocalAlias { get; set; }
            public bool AndroidGeneration1Suppressed { get; set; }
            public bool AndroidGeneration2Survives { get; set; }
            public bool TombstoneGeneration1Survives { get; set; }
            public bool UnresolvedIdentitySurvives { get; set; }
            public bool UnresolvedContributionSurvives { get; set; }
            public bool OptionalMetadataAbsentOnWire { get; set; }
        }
    }
}
