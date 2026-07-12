using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsSyncV2Tests
    {
        [Test]
        public void Deserialize_RejectsV1WithCloudV2Message()
        {
            var exception = Assert.Throws<FormatException>(() =>
                SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":1,""deviceId"":""v1"",""exportedAt"":0}"));
            Assert.That(exception.Message, Does.Contain("Unsupported schemaVersion: 1"));
            Assert.That(exception.Message, Does.Contain("Expected: 2"));
        }

        [Test]
        public void Deserialize_V2AcceptsUnsortedArrays()
        {
            var snapshot = SyncSnapshotSerializer.Deserialize(JsonConvert.SerializeObject(ValidV2()));
            Assert.That(snapshot.PlaybackStatistics.Devices[0].DeviceId, Is.EqualTo("device-b"));
            Assert.That(snapshot.PlaybackStatistics.Songs[0].Contributions[0].DeviceId, Is.EqualTo("device-b"));
        }

        [Test]
        public void Deserialize_V2AcceptsRawAndroidJson()
        {
            var snapshot = SyncSnapshotSerializer.Deserialize(@"{
  ""schemaVersion"": 2,
  ""deviceId"": ""android-exporter"",
  ""exportedAt"": 1700000000000,
  ""playlists"": [],
  ""loopPoints"": [],
  ""ratings"": [],
  ""playbackStatistics"": {
    ""dateBucketBasis"": ""sourceLocal"",
    ""devices"": [
      {
        ""deviceId"": ""android-phone"",
        ""currentGeneration"": 7,
        ""displayName"": ""Pixel 9"",
        ""displayNameUpdatedAtUtcMs"": 1700000000100,
        ""platform"": ""android"",
        ""firstSeenAtUtcMs"": 1699999999000,
        ""lastSeenAtUtcMs"": 1700000000200
      }
    ],
    ""songs"": [
      {
        ""song"": {
          ""fileName"": ""/storage/emulated/0/Music/Foo.MP3"",
          ""normalizedFileName"": ""foo.mp3"",
          ""durationMs"": 1234,
          ""totalSamples"": 555,
          ""contentHash"": ""abc123""
        },
        ""contributions"": [
          {
            ""deviceId"": ""android-phone"",
            ""generation"": 7,
            ""datedListenMs"": {
              ""2026-01-01"": 10,
              ""2026-01-03"": 30,
              ""2026-01-02"": 20
            },
            ""undatedListenMs"": 40,
            ""firstPlayedAtUtcMs"": 1700000000001,
            ""lastPlayedAtUtcMs"": 1700000000002,
            ""updatedAtUtcMs"": 1700000000003
          }
        ]
      }
    ],
    ""tombstones"": [
      {
        ""deviceId"": ""android-phone"",
        ""generation"": 6,
        ""scope"": ""deviceGeneration"",
        ""tombstonedAtUtcMs"": 1700000000004,
        ""tombstonedByDeviceId"": ""android-phone"",
        ""reason"": ""reset""
      }
    ]
  }
}");

            Assert.That(snapshot.PlaybackStatistics.Devices[0].Platform, Is.EqualTo("android"));
            Assert.That(snapshot.PlaybackStatistics.Songs[0].Song.NormalizedFileName, Is.EqualTo("foo.mp3"));
            Assert.That(snapshot.PlaybackStatistics.Tombstones[0].Scope, Is.EqualTo(SyncSnapshotSerializer.DeviceGenerationTombstoneScope));
        }

        [Test]
        public void Deserialize_V2AcceptsAndroidGoldenFixtureShape()
        {
            var snapshot = SyncSnapshotSerializer.Deserialize(AndroidGoldenFixtureJson());

            Assert.That(snapshot.PlaybackStatistics.DateBucketBasis, Is.EqualTo(SyncSnapshotSerializer.SourceLocalDateBucketBasis));
            Assert.That(snapshot.PlaybackStatistics.Songs, Has.Count.EqualTo(2));

            var cafe = snapshot.PlaybackStatistics.Songs.Single(x => x.Song.NormalizedFileName == "café.mp3");
            Assert.That(cafe.Contributions.Single(x => x.DeviceId == "android-pixel-8" && x.Generation == 2).DatedListenMs["2026-07-10"], Is.EqualTo(60000));

            var unresolved = snapshot.PlaybackStatistics.Songs.Single(x => x.Song.NormalizedFileName == "unresolved mix.flac");
            Assert.That(unresolved.Contributions.Single().UndatedListenMs, Is.EqualTo(9876));
        }

        [Test]
        public void Deserialize_RejectsUnknownSchema()
        {
            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":3,""deviceId"":""d"",""exportedAt"":0}"));
        }

        [TestCase(" C:\\Music\\Café.MP3 ", "café.mp3")]
        [TestCase("/music/SONG.MP3", "song.mp3")]
        public void NormalizePlaybackSongFileName_UsesBasenameTrimNfcAndInvariantLowercase(string input, string expected)
        {
            Assert.That(SyncSnapshotSerializer.NormalizePlaybackSongFileName(input), Is.EqualTo(expected));
        }

        [Test]
        public void Deserialize_V2RejectsMissingContainerAndInvalidBasis()
        {
            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Deserialize(@"{
              ""schemaVersion"": 2, ""deviceId"": ""missing"", ""exportedAt"": 1,
              ""playbackStatistics"": { ""dateBucketBasis"": ""source-local"", ""devices"": [], ""tombstones"": [] }
            }"));
            var invalidBasis = ValidV2(); invalidBasis.PlaybackStatistics.DateBucketBasis = "utc";
            AssertInvalid(invalidBasis);
        }

        [Test]
        public void Deserialize_V2RejectsIdentityCountersAndDates()
        {
            var normalization = ValidV2(); normalization.PlaybackStatistics.Songs[0].Song.NormalizedFileName = "wrong.mp3";
            AssertInvalid(normalization);
            var negative = ValidV2(); negative.PlaybackStatistics.Songs[0].Contributions[0].UndatedListenMs = -1;
            AssertInvalid(negative);
            var date = ValidV2(); date.PlaybackStatistics.Songs[0].Contributions[0].DatedListenMs = new Dictionary<string, long> { ["2026-02-32"] = 1 };
            AssertInvalid(date);
            var order = ValidV2(); order.PlaybackStatistics.Songs[0].Contributions[0].FirstPlayedAtUtcMs = 5; order.PlaybackStatistics.Songs[0].Contributions[0].LastPlayedAtUtcMs = 4;
            AssertInvalid(order);
        }

        [TestCase("")]
        [TestCase(" ")]
        public void Serialize_V2RejectsBlankDeviceDisplayName(string displayName)
        {
            var snapshot = ValidV2();
            snapshot.PlaybackStatistics.Devices[0].DisplayName = displayName;

            var exception = Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Serialize(snapshot));
            Assert.That(exception.Message, Does.Contain("displayName"));
        }

        [Test]
        public void Serialize_V2RejectsNullDeviceDisplayName()
        {
            var snapshot = ValidV2();
            snapshot.PlaybackStatistics.Devices[0].DisplayName = null;

            var exception = Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Serialize(snapshot));
            Assert.That(exception.Message, Does.Contain("displayName"));
        }

        [Test]
        public void Serialize_V2RejectsZeroDeviceDisplayNameTimestamp()
        {
            var snapshot = ValidV2();
            snapshot.PlaybackStatistics.Devices[0].DisplayNameUpdatedAtUtcMs = 0;

            var exception = Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Serialize(snapshot));
            Assert.That(exception.Message, Does.Contain("displayNameUpdatedAtUtcMs"));
        }

        [Test]
        public void Deserialize_V2RejectsDuplicateRawJsonProperties()
        {
            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Deserialize(@"{
  ""schemaVersion"": 2,
  ""schemaVersion"": 2,
  ""deviceId"": ""dup"",
  ""exportedAt"": 1,
  ""playlists"": [],
  ""loopPoints"": [],
  ""ratings"": [],
  ""playbackStatistics"": {
    ""dateBucketBasis"": ""source-local"",
    ""devices"": [],
    ""songs"": [],
    ""tombstones"": []
  }
}"));
        }

        [Test]
        public void Deserialize_V2RejectsInvalidDailyDateInRawJson()
        {
            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Deserialize(@"{
  ""schemaVersion"": 2,
  ""deviceId"": ""raw-date"",
  ""exportedAt"": 1,
  ""playlists"": [],
  ""loopPoints"": [],
  ""ratings"": [],
  ""playbackStatistics"": {
    ""dateBucketBasis"": ""source-local"",
    ""devices"": [
      {
        ""deviceId"": ""device-a"",
        ""currentGeneration"": 1,
        ""displayName"": ""PC"",
        ""displayNameUpdatedAtUtcMs"": 1,
        ""platform"": ""windows"",
        ""firstSeenAtUtcMs"": 1,
        ""lastSeenAtUtcMs"": 1
      }
    ],
    ""songs"": [
      {
        ""song"": {
          ""fileName"": ""Song.mp3"",
          ""normalizedFileName"": ""song.mp3"",
          ""durationMs"": 1000
        },
        ""contributions"": [
          {
            ""deviceId"": ""device-a"",
            ""generation"": 1,
            ""dailyListenMs"": {
              ""2026-13-01"": 5
            },
            ""undatedListenMs"": 0,
            ""updatedAtUtcMs"": 1
          }
        ]
      }
    ],
    ""tombstones"": []
  }
}"));
        }

        [Test]
        public void Deserialize_V2RejectsDuplicateSemanticKeys()
        {
            var devices = ValidV2(); devices.PlaybackStatistics.Devices.Add(new SyncPlaybackDevice { DeviceId = "device-a", CurrentGeneration = 9, DisplayName = "Duplicate", DisplayNameUpdatedAtUtcMs = 9, Platform = "windows", FirstSeenAtUtcMs = 9, LastSeenAtUtcMs = 9 });
            AssertInvalid(devices);
            var songs = ValidV2(); songs.PlaybackStatistics.Songs.Add(songs.PlaybackStatistics.Songs[0]);
            AssertInvalid(songs);
            var contributions = ValidV2(); contributions.PlaybackStatistics.Songs[0].Contributions.Add(new SyncPlaybackContribution { DeviceId = "device-a", Generation = 1, DatedListenMs = new Dictionary<string, long>(), UndatedListenMs = 0, UpdatedAtUtcMs = 1 });
            AssertInvalid(contributions);
            var tombstones = ValidV2(); tombstones.PlaybackStatistics.Tombstones.Add(new SyncPlaybackTombstone { DeviceId = "device-a", Generation = 0, Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope, TombstonedAtUtcMs = 1, TombstonedByDeviceId = "device-a", Reason = "duplicate" });
            AssertInvalid(tombstones);
        }

        [Test]
        public void Deserialize_V2RejectsUnknownDeviceAndTombstoneScope()
        {
            var contribution = ValidV2(); contribution.PlaybackStatistics.Songs[0].Contributions[0].DeviceId = "unknown";
            AssertInvalid(contribution);
            var tombstone = ValidV2(); tombstone.PlaybackStatistics.Tombstones[0].Scope = "song";
            AssertInvalid(tombstone);
        }

        [Test]
        public void Serialize_V2CanonicalizesOrderingAndDoesNotEmitAlias()
        {
            var snapshot = ValidV2();
            var json = SyncSnapshotSerializer.Serialize(snapshot);

            Assert.That(json, Does.Not.Contain("playbackStats"));
            Assert.That(json.IndexOf("device-a", StringComparison.Ordinal), Is.LessThan(json.IndexOf("device-b", StringComparison.Ordinal)));
            Assert.That(json.IndexOf("2026-01-01", StringComparison.Ordinal), Is.LessThan(json.IndexOf("2026-01-02", StringComparison.Ordinal)));
            Assert.That(snapshot.PlaybackStatistics.Devices[0].DeviceId, Is.EqualTo("device-b"), "serialization must not mutate callers");
        }

        [Test]
        public void Merge_RejectsV1Input()
        {
            var v1 = new SyncSnapshot { SchemaVersion = 1, DeviceId = "v1", ExportedAt = 1 };
            Assert.Throws<FormatException>(() => SyncMergeEngine.Merge(v1, ValidV2()));
            Assert.Throws<FormatException>(() => SyncMergeEngine.Merge(ValidV2(), v1));
            Assert.Throws<ArgumentNullException>(() => SyncMergeEngine.Merge(null, ValidV2()));
            Assert.Throws<ArgumentNullException>(() => SyncMergeEngine.Merge(ValidV2(), null));
        }

        [Test]
        public void Serialize_RejectsNonV2AndMalformedV2()
        {
            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Serialize(
                new SyncSnapshot { SchemaVersion = 1, DeviceId = "v1", ExportedAt = 1 }));
            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Serialize(
                new SyncSnapshot { SchemaVersion = 2, DeviceId = "v2", ExportedAt = 1 }));
        }

        [Test]
        public void PlaybackStatisticsMerge_UsesMaxRulesAndDeterministicMetadata()
        {
            var left = ValidV2().PlaybackStatistics;
            var right = ValidV2().PlaybackStatistics;
            left.Songs[0].Song.FileName = "Alpha.mp3";
            left.Songs[0].Song.ContentHash = "aaa";
            left.Songs[0].Song.TotalSamples = 10;
            left.Songs[0].Contributions[1].DatedListenMs["2026-01-01"] = 4;
            left.Songs[0].Contributions[1].UndatedListenMs = 4;
            left.Songs[0].Contributions[1].FirstPlayedAtUtcMs = 4;
            left.Songs[0].Contributions[1].LastPlayedAtUtcMs = 4;
            left.Songs[0].Contributions[1].UpdatedAtUtcMs = 4;
            right.Songs[0].Song.FileName = "Zulu.mp3";
            right.Songs[0].Song.ContentHash = "zzz";
            right.Songs[0].Song.TotalSamples = 20;
            right.Songs[0].Contributions[1].DatedListenMs["2026-01-01"] = 9;
            right.Songs[0].Contributions[1].UndatedListenMs = 9;
            right.Songs[0].Contributions[1].FirstPlayedAtUtcMs = 2;
            right.Songs[0].Contributions[1].LastPlayedAtUtcMs = 8;
            right.Songs[0].Contributions[1].UpdatedAtUtcMs = 8;
            right.Devices[1].DisplayName = "Zed";
            right.Devices[1].DisplayNameUpdatedAtUtcMs = 9;

            var merged = PlaybackStatisticsSyncCanonicalizer.Merge(left, right);
            var song = merged.Songs[0];
            var contribution = song.Contributions.Single(x => x.DeviceId == "device-a");
            Assert.That(song.Song.FileName, Is.EqualTo("Zulu.mp3"));
            Assert.That(song.Song.ContentHash, Is.EqualTo("zzz"));
            Assert.That(song.Song.TotalSamples, Is.EqualTo(20));
            Assert.That(contribution.DatedListenMs["2026-01-01"], Is.EqualTo(9));
            Assert.That(contribution.UndatedListenMs, Is.EqualTo(9));
            Assert.That(contribution.FirstPlayedAtUtcMs, Is.EqualTo(2));
            Assert.That(contribution.LastPlayedAtUtcMs, Is.EqualTo(8));
            Assert.That(merged.Devices.Single(x => x.DeviceId == "device-a").DisplayName, Is.EqualTo("Zed"));
        }

        [Test]
        public void PlaybackStatisticsMerge_ThreeInputs_AllPermutationsAreCanonicalAndPreserveContributions()
        {
            var first = ValidV2().PlaybackStatistics;
            var second = ValidV2().PlaybackStatistics;
            var third = ValidV2().PlaybackStatistics;

            first.Songs[0].Song.FileName = @"C:\Music\Song.MP3";
            first.Songs[0].Song.TotalSamples = null;
            first.Songs[0].Song.ContentHash = null;
            first.Songs[0].Contributions[1].DatedListenMs = new Dictionary<string, long> { ["2026-01-01"] = 10 };
            first.Songs[0].Contributions[1].UndatedListenMs = 11;
            first.Songs[0].Contributions[1].FirstPlayedAtUtcMs = 30;
            first.Songs[0].Contributions[1].LastPlayedAtUtcMs = 40;
            first.Songs[0].Contributions[1].UpdatedAtUtcMs = 50;

            second.Songs[0].Song.FileName = @"D:\Music\Song.MP3";
            second.Songs[0].Song.TotalSamples = 10;
            second.Songs[0].Song.ContentHash = "hash-b";
            second.Songs[0].Contributions[1].DatedListenMs = new Dictionary<string, long> { ["2026-01-01"] = 20 };
            second.Songs[0].Contributions[1].UndatedListenMs = 22;
            second.Songs[0].Contributions[1].FirstPlayedAtUtcMs = 20;
            second.Songs[0].Contributions[1].LastPlayedAtUtcMs = 60;
            second.Songs[0].Contributions[1].UpdatedAtUtcMs = 60;

            third.Songs[0].Song.FileName = @"E:\Music\Song.MP3";
            third.Songs[0].Song.TotalSamples = 20;
            third.Songs[0].Song.ContentHash = "hash-c";
            third.Songs[0].Contributions[1].DatedListenMs = new Dictionary<string, long> { ["2026-01-01"] = 30 };
            third.Songs[0].Contributions[1].UndatedListenMs = 33;
            third.Songs[0].Contributions[1].FirstPlayedAtUtcMs = 10;
            third.Songs[0].Contributions[1].LastPlayedAtUtcMs = 50;
            third.Songs[0].Contributions[1].UpdatedAtUtcMs = 70;

            var inputs = new[] { first, second, third };
            var canonical = string.Empty;
            var permutationCount = 0;

            for (var i = 0; i < inputs.Length; i++)
            {
                for (var j = 0; j < inputs.Length; j++)
                {
                    if (j == i) continue;
                    for (var k = 0; k < inputs.Length; k++)
                    {
                        if (k == i || k == j) continue;

                        var merged = PlaybackStatisticsSyncCanonicalizer.Merge(
                            PlaybackStatisticsSyncCanonicalizer.Merge(inputs[i], inputs[j]), inputs[k]);
                        var serialized = SerializeStatistics(merged);
                        if (permutationCount == 0) canonical = serialized;
                        Assert.That(serialized, Is.EqualTo(canonical));
                        permutationCount++;

                        var song = merged.Songs.Single(x => x.Song.NormalizedFileName == "song.mp3");
                        Assert.That(song.Song.FileName, Is.EqualTo(@"E:\Music\Song.MP3"));
                        Assert.That(song.Song.TotalSamples, Is.EqualTo(20));
                        Assert.That(song.Song.ContentHash, Is.EqualTo("hash-c"));

                        var mergedContribution = song.Contributions.Single(x => x.DeviceId == "device-a" && x.Generation == 1);
                        Assert.That(mergedContribution.DatedListenMs["2026-01-01"], Is.EqualTo(30));
                        Assert.That(mergedContribution.UndatedListenMs, Is.EqualTo(33));
                        Assert.That(mergedContribution.FirstPlayedAtUtcMs, Is.EqualTo(10));
                        Assert.That(mergedContribution.LastPlayedAtUtcMs, Is.EqualTo(60));
                        Assert.That(mergedContribution.UpdatedAtUtcMs, Is.EqualTo(70));
                        Assert.That(song.Contributions.Single(x => x.DeviceId == "device-b" && x.Generation == 2).UndatedListenMs, Is.EqualTo(0));
                    }
                }
            }

            Assert.That(permutationCount, Is.EqualTo(6));
        }

        [Test]
        public void PlaybackStatisticsMerge_AndroidFixturesSuppressTombstonedGenerationInBothDirections()
        {
            var golden = SyncSnapshotSerializer.Deserialize(AndroidGoldenFixtureJson()).PlaybackStatistics;
            var collision = SyncSnapshotSerializer.Deserialize(AndroidCollisionFixtureJson()).PlaybackStatistics;

            var forward = PlaybackStatisticsSyncCanonicalizer.Merge(golden, collision);
            var reverse = PlaybackStatisticsSyncCanonicalizer.Merge(collision, golden);

            Assert.That(SerializeStatistics(forward), Is.EqualTo(SerializeStatistics(reverse)));
            AssertMergedFixtureStatistics(forward);
            AssertMergedFixtureStatistics(reverse);
        }

        [Test]
        public void PlaybackStatisticsMerge_TombstoneSuppressesAllExactContributionsButKeepsSong()
        {
            var left = ValidV2().PlaybackStatistics;
            left.Tombstones.Clear();
            var right = ValidV2().PlaybackStatistics;
            right.Songs.Clear();
            right.Tombstones = new List<SyncPlaybackTombstone>
            {
                new SyncPlaybackTombstone { DeviceId = "device-a", Generation = 1, Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope, TombstonedAtUtcMs = 9, TombstonedByDeviceId = "device-a", Reason = "clear" }
            };

            var merged = PlaybackStatisticsSyncCanonicalizer.Merge(left, right);
            Assert.That(merged.Songs, Has.Count.EqualTo(1));
            Assert.That(merged.Songs[0].Contributions.Any(x => x.DeviceId == "device-a" && x.Generation == 1), Is.False);
        }

        [Test]
        public void PlaybackStatisticsMerge_RejectsPlatformConflict()
        {
            var left = ValidV2().PlaybackStatistics;
            var right = ValidV2().PlaybackStatistics;
            right.Devices[1].Platform = "android";
            Assert.Throws<FormatException>(() => PlaybackStatisticsSyncCanonicalizer.Merge(left, right));
        }

        [Test]
        public void PlaybackStatisticsMerge_IsIdempotentCommutativeAndAssociative()
        {
            var a = ValidV2().PlaybackStatistics;
            var b = ValidV2().PlaybackStatistics;
            var c = ValidV2().PlaybackStatistics;
            b.Songs[0].Contributions[1].UndatedListenMs = 7;
            c.Songs[0].Contributions[1].DatedListenMs["2026-01-03"] = 5;

            Assert.That(SerializeStatistics(PlaybackStatisticsSyncCanonicalizer.Merge(a, a)), Is.EqualTo(SerializeStatistics(a)));
            Assert.That(SerializeStatistics(PlaybackStatisticsSyncCanonicalizer.Merge(a, b)), Is.EqualTo(SerializeStatistics(PlaybackStatisticsSyncCanonicalizer.Merge(b, a))));
            Assert.That(SerializeStatistics(PlaybackStatisticsSyncCanonicalizer.Merge(PlaybackStatisticsSyncCanonicalizer.Merge(a, b), c)), Is.EqualTo(SerializeStatistics(PlaybackStatisticsSyncCanonicalizer.Merge(a, PlaybackStatisticsSyncCanonicalizer.Merge(b, c)))));
        }

        private static string SerializeStatistics(SyncPlaybackStatistics statistics)
        {
            return SyncSnapshotSerializer.Serialize(new SyncSnapshot { SchemaVersion = 2, DeviceId = "test", ExportedAt = 1, PlaybackStatistics = statistics });
        }

        private static void AssertMergedFixtureStatistics(SyncPlaybackStatistics statistics)
        {
            var cafe = statistics.Songs.Single(x => x.Song.NormalizedFileName == "café.mp3");
            Assert.That(cafe.Contributions.Any(x => x.DeviceId == "android-pixel-8" && x.Generation == 1), Is.False);
            Assert.That(cafe.Contributions.Single(x => x.DeviceId == "android-pixel-8" && x.Generation == 2).DatedListenMs["2026-07-10"], Is.EqualTo(60000));
            Assert.That(statistics.Songs.Any(x => x.Song.NormalizedFileName == "unresolved mix.flac"), Is.True);
        }

        private static SyncSnapshot Deserialize(SyncSnapshot snapshot) => SyncSnapshotSerializer.Deserialize(SyncSnapshotSerializer.Serialize(snapshot));
        private static void AssertInvalid(SyncSnapshot snapshot) => Assert.Throws<FormatException>(() => Deserialize(snapshot));

        private static string AndroidGoldenFixtureJson() => @"{
  ""schemaVersion"": 2,
  ""deviceId"": ""android-pixel-8"",
  ""exportedAt"": 1783779600000,
  ""playlists"": [],
  ""loopPoints"": [],
  ""ratings"": [],
  ""playbackStatistics"": {
    ""dateBucketBasis"": ""sourceLocal"",
    ""devices"": [
      {
        ""deviceId"": ""android-pixel-8"",
        ""displayName"": ""Google Pixel 8"",
        ""firstSeenAtUtcMs"": 1783588500000,
        ""lastSeenAtUtcMs"": 1783779600000,
        ""currentGeneration"": 2,
        ""platform"": ""android"",
        ""displayNameUpdatedAtUtcMs"": 1783588500000
      },
      {
        ""deviceId"": ""desktop-wpf-1"",
        ""displayName"": ""Windows desktop"",
        ""firstSeenAtUtcMs"": 1783588500000,
        ""lastSeenAtUtcMs"": 1783622400000,
        ""currentGeneration"": 1,
        ""platform"": ""windows"",
        ""displayNameUpdatedAtUtcMs"": 1783588500000
      }
    ],
    ""songs"": [
      {
        ""song"": {
          ""fileName"": ""  CAFÉ.MP3  "",
          ""durationMs"": 123456,
          ""totalSamples"": 5444400,
          ""normalizedFileName"": ""café.mp3""
        },
        ""contributions"": [
          {
            ""deviceId"": ""android-pixel-8"",
            ""generation"": 2,
            ""datedListenMs"": { ""2026-07-10"": 60000, ""2026-07-11"": 30000 },
            ""undatedListenMs"": 12000,
            ""firstPlayedAtUtcMs"": 1783717800000,
            ""lastPlayedAtUtcMs"": 1783779600000,
            ""updatedAtUtcMs"": 1783779600000
          },
          {
            ""deviceId"": ""desktop-wpf-1"",
            ""generation"": 1,
            ""datedListenMs"": { ""2026-07-09"": 45000 },
            ""undatedListenMs"": 0,
            ""firstPlayedAtUtcMs"": 1783622400000,
            ""lastPlayedAtUtcMs"": 1783622400000,
            ""updatedAtUtcMs"": 1783622400000
          }
        ]
      },
      {
        ""song"": {
          ""fileName"": ""Unresolved Mix.FLAC"",
          ""durationMs"": 654321,
          ""normalizedFileName"": ""unresolved mix.flac""
        },
        ""contributions"": [
          {
            ""deviceId"": ""desktop-wpf-1"",
            ""generation"": 0,
            ""datedListenMs"": {},
            ""undatedListenMs"": 9876,
            ""firstPlayedAtUtcMs"": 1783622400000,
            ""lastPlayedAtUtcMs"": 1783622400000,
            ""updatedAtUtcMs"": 1783622400000
          }
        ]
      }
    ],
    ""tombstones"": [
      {
        ""deviceId"": ""android-pixel-8"",
        ""generation"": 1,
        ""tombstonedAtUtcMs"": 1783672200000,
        ""scope"": ""deviceGeneration"",
        ""tombstonedByDeviceId"": ""android-pixel-8"",
        ""reason"": ""local_clear""
      }
    ]
  }
}";

        private static string AndroidCollisionFixtureJson() => @"{
  ""schemaVersion"": 2,
  ""deviceId"": ""desktop-wpf-1"",
  ""exportedAt"": 1783779600000,
  ""playlists"": [],
  ""loopPoints"": [],
  ""ratings"": [],
  ""playbackStatistics"": {
    ""dateBucketBasis"": ""sourceLocal"",
    ""devices"": [
      {
        ""deviceId"": ""android-pixel-8"",
        ""displayName"": ""Google Pixel 8"",
        ""firstSeenAtUtcMs"": 1783588500000,
        ""lastSeenAtUtcMs"": 1783672200000,
        ""currentGeneration"": 2,
        ""platform"": ""android"",
        ""displayNameUpdatedAtUtcMs"": 1783588500000
      },
      {
        ""deviceId"": ""desktop-wpf-1"",
        ""displayName"": ""Windows desktop"",
        ""firstSeenAtUtcMs"": 1783588500000,
        ""lastSeenAtUtcMs"": 1783779600000,
        ""currentGeneration"": 1,
        ""platform"": ""windows"",
        ""displayNameUpdatedAtUtcMs"": 1783588500000
      }
    ],
    ""songs"": [
      {
        ""song"": {
          ""fileName"": ""  CAFÉ.MP3  "",
          ""durationMs"": 123456,
          ""totalSamples"": 5444400,
          ""normalizedFileName"": ""café.mp3""
        },
        ""contributions"": [
          {
            ""deviceId"": ""android-pixel-8"",
            ""generation"": 1,
            ""datedListenMs"": { ""2026-07-09"": 15000 },
            ""undatedListenMs"": 0,
            ""firstPlayedAtUtcMs"": 1783622400000,
            ""lastPlayedAtUtcMs"": 1783622400000,
            ""updatedAtUtcMs"": 1783622400000
          }
        ]
      }
    ],
    ""tombstones"": []
  }
}";

        private static SyncSnapshot ValidV2()
        {
            return new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "exporter", ExportedAt = 1,
                PlaybackStatistics = new SyncPlaybackStatistics
                {
                    DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                    Devices = new List<SyncPlaybackDevice>
                    {
                        new SyncPlaybackDevice { DeviceId = "device-b", CurrentGeneration = 2, DisplayName = "Phone", DisplayNameUpdatedAtUtcMs = 2, Platform = "android", FirstSeenAtUtcMs = 2, LastSeenAtUtcMs = 2 },
                        new SyncPlaybackDevice { DeviceId = "device-a", CurrentGeneration = 1, DisplayName = "Desktop", DisplayNameUpdatedAtUtcMs = 1, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 }
                    },
                    Songs = new List<SyncPlaybackSong>
                    {
                        new SyncPlaybackSong
                        {
                            Song = new SyncPlaybackSongIdentity { FileName = @"C:\Music\Song.MP3", NormalizedFileName = "song.mp3", DurationMs = 1000 },
                            Contributions = new List<SyncPlaybackContribution>
                            {
                                new SyncPlaybackContribution { DeviceId = "device-b", Generation = 2, DatedListenMs = new Dictionary<string, long> { ["2026-01-02"] = 2, ["2026-01-01"] = 1 }, UndatedListenMs = 0, UpdatedAtUtcMs = 2 },
                                new SyncPlaybackContribution { DeviceId = "device-a", Generation = 1, DatedListenMs = new Dictionary<string, long>(), UndatedListenMs = 1, FirstPlayedAtUtcMs = 1, LastPlayedAtUtcMs = 2, UpdatedAtUtcMs = 2 }
                            }
                        }
                    },
                    Tombstones = new List<SyncPlaybackTombstone>
                    {
                        new SyncPlaybackTombstone { DeviceId = "device-a", Generation = 0, Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope, TombstonedAtUtcMs = 1, TombstonedByDeviceId = "device-a", Reason = "reset" }
                    }
                }
            };
        }
    }
}
