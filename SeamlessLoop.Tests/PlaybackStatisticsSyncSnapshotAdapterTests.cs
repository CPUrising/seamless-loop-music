using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackStatisticsSyncSnapshotAdapterTests
    {
        private string _dbPath;
        private DatabaseHelper _database;
        private PlaybackStatisticsSyncRepository _repository;
        private PlaybackStatisticsSyncSnapshotAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "PlaybackSyncAdapter_" + Guid.NewGuid().ToString("N") + ".db");
            _database = new DatabaseHelper(_dbPath);
            _database.InitializeDatabase();
            _repository = new PlaybackStatisticsSyncRepository(_dbPath);
            _adapter = new PlaybackStatisticsSyncSnapshotAdapter(_database);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void Export_EmptyContainers_ArePresent()
        {
            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                Assert.That(exported.DateBucketBasis, Is.EqualTo(SyncSnapshotSerializer.SourceLocalDateBucketBasis));
                Assert.That(exported.Devices, Is.Empty);
                Assert.That(exported.Songs, Is.Empty);
                Assert.That(exported.Tombstones, Is.Empty);
            }
        }

        [Test]
        public void Export_RepairsBlankCurrentLocalDeviceBeforeQuery()
        {
            _database.SetSetting("Sync.DeviceId", "local-device");
            _repository.EnsureDevice(new PlaybackSyncDevice { DeviceId = "local-device", CurrentGeneration = 0, DisplayName = " ", DisplayNameUpdatedAtUtcMs = 0, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });

            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                var device = exported.Devices.Single(x => x.DeviceId == "local-device");
                Assert.That(device.DisplayName, Is.Not.Null.And.Not.Empty);
                Assert.That(device.DisplayName.Trim(), Is.Not.Empty);
                Assert.That(device.DisplayNameUpdatedAtUtcMs, Is.GreaterThan(0));
                Assert.DoesNotThrow(() => SerializeStatistics(exported));
            }
        }

        [Test]
        public void Export_PreservesValidRenamedCurrentLocalDevice()
        {
            _database.SetSetting("Sync.DeviceId", "local-device");
            _repository.EnsureDevice(new PlaybackSyncDevice { DeviceId = "local-device", CurrentGeneration = 0, DisplayName = "Renamed desktop", DisplayNameUpdatedAtUtcMs = 42, Platform = "windows", FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });

            using (var connection = _database.GetConnection())
            {
                var device = _adapter.Export(connection).Devices.Single(x => x.DeviceId == "local-device");
                Assert.That(device.DisplayName, Is.EqualTo("Renamed desktop"));
                Assert.That(device.DisplayNameUpdatedAtUtcMs, Is.EqualTo(42));
            }
        }

        [TestCase("")]
        [TestCase(" ")]
        public void Apply_RejectsBlankDisplayName(string displayName)
        {
            var statistics = new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { Device("device-a", "windows") },
                Songs = new List<SyncPlaybackSong>(),
                Tombstones = new List<SyncPlaybackTombstone>()
            };
            statistics.Devices[0].DisplayName = displayName;

            using (var connection = _database.GetConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var exception = Assert.Throws<FormatException>(() => _adapter.Apply(connection, transaction, statistics));
                Assert.That(exception.Message, Does.Contain("displayName"));
                transaction.Rollback();
            }
        }

        [Test]
        public void Apply_RejectsNullDisplayName()
        {
            var statistics = new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { Device("device-a", "windows") },
                Songs = new List<SyncPlaybackSong>(),
                Tombstones = new List<SyncPlaybackTombstone>()
            };
            statistics.Devices[0].DisplayName = null;

            using (var connection = _database.GetConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var exception = Assert.Throws<FormatException>(() => _adapter.Apply(connection, transaction, statistics));
                Assert.That(exception.Message, Does.Contain("displayName"));
                transaction.Rollback();
            }
        }

        [Test]
        public void Apply_RejectsZeroDisplayNameTimestamp()
        {
            var statistics = new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { Device("device-a", "windows") },
                Songs = new List<SyncPlaybackSong>(),
                Tombstones = new List<SyncPlaybackTombstone>()
            };
            statistics.Devices[0].DisplayNameUpdatedAtUtcMs = 0;

            using (var connection = _database.GetConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var exception = Assert.Throws<FormatException>(() => _adapter.Apply(connection, transaction, statistics));
                Assert.That(exception.Message, Does.Contain("displayNameUpdatedAtUtcMs"));
                transaction.Rollback();
            }
        }

        [Test]
        public void Export_AndroidGoldenRoundTrip_PreservesCanonicalWire()
        {
            var snapshot = SyncSnapshotSerializer.Deserialize(AndroidGoldenFixtureJson());
            ApplyStatisticsSnapshot(snapshot.PlaybackStatistics);

            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                var exportedJson = SerializeStatistics(exported);
                var expectedJson = SerializeStatistics(snapshot.PlaybackStatistics);
                Assert.That(exportedJson, Is.EqualTo(expectedJson));
            }
        }

        [Test]
        public void Export_PreservesDistinctExactDurationsAndSeparateContributionsForOneTrack()
        {
            var trackId = InsertTrack("track.mp3", 239986);
            var first = _repository.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "track.mp3", DurationMs = 239986 }, trackId);
            var second = _repository.EnsureSongBoundToTrack(new PlaybackSyncSong { FileName = "track.mp3", DurationMs = 239987 }, trackId);
            Register("device-a");
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = first.SongId, DeviceId = "device-a", Generation = 0, UndatedListenMs = 3, UpdatedAtUtcMs = 3 });
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = second.SongId, DeviceId = "device-a", Generation = 0, UndatedListenMs = 7, UpdatedAtUtcMs = 7 });

            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                Assert.That(exported.Songs, Has.Count.EqualTo(2));
                Assert.That(exported.Songs.Single(x => x.Song.DurationMs == 239986).Contributions.Single().UndatedListenMs, Is.EqualTo(3));
                Assert.That(exported.Songs.Single(x => x.Song.DurationMs == 239987).Contributions.Single().UndatedListenMs, Is.EqualTo(7));
            }

            var state = _repository.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == first.SongId).LocalTrackId, Is.EqualTo(trackId));
            Assert.That(state.Songs.Single(x => x.SongId == second.SongId).LocalTrackId, Is.EqualTo(trackId));
        }

        [Test]
        public void Apply_UnresolvedNode_SurvivesWithNullLocalTrackId()
        {
            var snapshot = SyncSnapshotSerializer.Deserialize(AndroidGoldenFixtureJson());
            ApplyStatisticsSnapshot(snapshot.PlaybackStatistics);

            var unresolved = _repository.LoadState().Songs.Single(x => x.NormalizedFileName == "unresolved mix.flac");
            Assert.That(unresolved.LocalTrackId, Is.Null);
        }

        [Test]
        public void Apply_TombstoneCollision_RemovesExistingContribution()
        {
            var song = EnsureSong("cafe.mp3", 1234);
            Register("device-a");
            Register("actor");
            _repository.MergeContribution(new PlaybackSyncContribution
            {
                SongId = song.SongId,
                DeviceId = "device-a",
                Generation = 1,
                UndatedListenMs = 5,
                UpdatedAtUtcMs = 5,
                DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-01-01", ListenMs = 5 } }
            });

            ApplyStatisticsSnapshot(new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice>
                {
                    Device("device-a", "windows"),
                    Device("actor", "windows")
                },
                Songs = new List<SyncPlaybackSong>(),
                Tombstones = new List<SyncPlaybackTombstone>
                {
                    new SyncPlaybackTombstone
                    {
                        DeviceId = "device-a",
                        Generation = 1,
                        Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope,
                        TombstonedAtUtcMs = 10,
                        TombstonedByDeviceId = "actor",
                        Reason = "clear"
                    }
                }
            });

            var state = _repository.LoadState();
            Assert.That(state.Contributions.Any(x => x.DeviceId == "device-a" && x.Generation == 1), Is.False);
            Assert.That(state.Songs.Any(x => x.SongId == song.SongId), Is.True);
        }

        [Test]
        public void Apply_TombstoneCollision_SameBatch_SkipsIncomingContribution()
        {
            ApplyStatisticsSnapshot(new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice>
                {
                    Device("device-a", "windows"),
                    Device("actor", "windows")
                },
                Songs = new List<SyncPlaybackSong>
                {
                    new SyncPlaybackSong
                    {
                        Song = new SyncPlaybackSongIdentity { FileName = "same.mp3", NormalizedFileName = "same.mp3", DurationMs = 1000 },
                        Contributions = new List<SyncPlaybackContribution>
                        {
                            new SyncPlaybackContribution
                            {
                                DeviceId = "device-a",
                                Generation = 1,
                                DatedListenMs = new Dictionary<string, long> { ["2026-02-01"] = 20 },
                                UndatedListenMs = 10,
                                UpdatedAtUtcMs = 20
                            }
                        }
                    }
                },
                Tombstones = new List<SyncPlaybackTombstone>
                {
                    new SyncPlaybackTombstone
                    {
                        DeviceId = "device-a",
                        Generation = 1,
                        Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope,
                        TombstonedAtUtcMs = 21,
                        TombstonedByDeviceId = "actor",
                        Reason = "clear"
                    }
                }
            });

            var state = _repository.LoadState();
            Assert.That(state.Contributions, Is.Empty);
            Assert.That(state.Songs, Has.Count.EqualTo(1));
            Assert.That(state.Tombstones, Has.Count.EqualTo(1));
        }

        [Test]
        public void Apply_IsIdempotent()
        {
            var statistics = SyncSnapshotSerializer.Deserialize(AndroidGoldenFixtureJson()).PlaybackStatistics;
            ApplyStatisticsSnapshot(statistics);
            string first;
            using (var firstConnection = _database.GetConnection())
                first = SerializeStatistics(_adapter.Export(firstConnection));

            ApplyStatisticsSnapshot(statistics);
            using (var connection = _database.GetConnection())
            {
                var second = SerializeStatistics(_adapter.Export(connection));
                Assert.That(second, Is.EqualTo(first));
            }
        }

        [Test]
        public void Apply_MaxMerge_UsesMaxPerContributionAndMetadata()
        {
            ApplyStatisticsSnapshot(new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { Device("device-a", "windows") },
                Songs = new List<SyncPlaybackSong>
                {
                    new SyncPlaybackSong
                    {
                        Song = new SyncPlaybackSongIdentity { FileName = "Alpha.mp3", NormalizedFileName = "alpha.mp3", DurationMs = 1000, TotalSamples = 10, ContentHash = "aaa" },
                        Contributions = new List<SyncPlaybackContribution>
                        {
                            new SyncPlaybackContribution
                            {
                                DeviceId = "device-a",
                                Generation = 1,
                                DatedListenMs = new Dictionary<string, long> { ["2026-03-01"] = 4 },
                                UndatedListenMs = 4,
                                FirstPlayedAtUtcMs = 4,
                                LastPlayedAtUtcMs = 4,
                                UpdatedAtUtcMs = 4
                            }
                        }
                    }
                },
                Tombstones = new List<SyncPlaybackTombstone>()
            });

            ApplyStatisticsSnapshot(new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { new SyncPlaybackDevice { DeviceId = "device-a", CurrentGeneration = 2, DisplayName = "Zed", DisplayNameUpdatedAtUtcMs = 9, Platform = "windows", FirstSeenAtUtcMs = 0, LastSeenAtUtcMs = 9 } },
                Songs = new List<SyncPlaybackSong>
                {
                    new SyncPlaybackSong
                    {
                        Song = new SyncPlaybackSongIdentity { FileName = @"C:\Music\alpha.mp3", NormalizedFileName = "alpha.mp3", DurationMs = 1000, TotalSamples = 20, ContentHash = "zzz" },
                        Contributions = new List<SyncPlaybackContribution>
                        {
                            new SyncPlaybackContribution
                            {
                                DeviceId = "device-a",
                                Generation = 1,
                                DatedListenMs = new Dictionary<string, long> { ["2026-03-01"] = 9 },
                                UndatedListenMs = 9,
                                FirstPlayedAtUtcMs = 2,
                                LastPlayedAtUtcMs = 8,
                                UpdatedAtUtcMs = 8
                            }
                        }
                    }
                },
                Tombstones = new List<SyncPlaybackTombstone>()
            });

            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                var song = exported.Songs.Single();
                var contribution = song.Contributions.Single();
                Assert.That(song.Song.FileName, Is.EqualTo(@"C:\Music\alpha.mp3"));
                Assert.That(song.Song.TotalSamples, Is.EqualTo(20));
                Assert.That(song.Song.ContentHash, Is.EqualTo("zzz"));
                Assert.That(contribution.DatedListenMs["2026-03-01"], Is.EqualTo(9));
                Assert.That(contribution.UndatedListenMs, Is.EqualTo(9));
                Assert.That(contribution.FirstPlayedAtUtcMs, Is.EqualTo(2));
                Assert.That(contribution.LastPlayedAtUtcMs, Is.EqualTo(8));
                Assert.That(exported.Devices.Single().DisplayName, Is.EqualTo("Zed"));
                Assert.That(exported.Devices.Single().CurrentGeneration, Is.EqualTo(2));
            }
        }

        [Test]
        public void Apply_SourceLocalLabels_Unchanged()
        {
            ApplyStatisticsSnapshot(new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { Device("device-a", "windows") },
                Songs = new List<SyncPlaybackSong>
                {
                    new SyncPlaybackSong
                    {
                        Song = new SyncPlaybackSongIdentity { FileName = "labels.mp3", NormalizedFileName = "labels.mp3", DurationMs = 1000 },
                        Contributions = new List<SyncPlaybackContribution>
                        {
                            new SyncPlaybackContribution
                            {
                                DeviceId = "device-a",
                                Generation = 0,
                                DatedListenMs = new Dictionary<string, long> { ["2026-07-10"] = 1, ["2026-07-11"] = 2 },
                                UndatedListenMs = 0,
                                UpdatedAtUtcMs = 2
                            }
                        }
                    }
                },
                Tombstones = new List<SyncPlaybackTombstone>()
            });

            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                Assert.That(exported.DateBucketBasis, Is.EqualTo(SyncSnapshotSerializer.SourceLocalDateBucketBasis));
                Assert.That(exported.Songs.Single().Contributions.Single().DatedListenMs.Keys, Is.EquivalentTo(new[] { "2026-07-10", "2026-07-11" }));
            }
        }

        [Test]
        public void Apply_PlatformConflict_RollsBackWholeStatisticsTransaction()
        {
            Register("device-a", "windows");

            using (var connection = _database.GetConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var ex = Assert.Throws<FormatException>(() => _adapter.Apply(connection, transaction, new SyncPlaybackStatistics
                {
                    DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                    Devices = new List<SyncPlaybackDevice> { Device("device-a", "android") },
                    Songs = new List<SyncPlaybackSong>
                    {
                        new SyncPlaybackSong
                        {
                            Song = new SyncPlaybackSongIdentity { FileName = "rollback.mp3", NormalizedFileName = "rollback.mp3", DurationMs = 1000 },
                            Contributions = new List<SyncPlaybackContribution>()
                        }
                    },
                    Tombstones = new List<SyncPlaybackTombstone>()
                }));
                Assert.That(ex.Message, Does.Contain("Conflicting platforms"));
                transaction.Rollback();
            }

            var state = _repository.LoadState();
            Assert.That(state.Devices, Has.Count.EqualTo(1));
            Assert.That(state.Songs, Is.Empty);
            Assert.That(state.Contributions, Is.Empty);
        }

        [Test]
        public void Apply_RelinkAfterCommit_UsesExistingExactThenUniqueFuzzy()
        {
            InsertTrack("Song.mp3", 1000);
            InsertTrack("Approx.mp3", 1090);

            ApplyStatisticsSnapshot(new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = new List<SyncPlaybackDevice> { Device("device-a", "windows") },
                Songs = new List<SyncPlaybackSong>
                {
                    new SyncPlaybackSong
                    {
                        Song = new SyncPlaybackSongIdentity { FileName = @"C:\Music\Song.mp3", NormalizedFileName = "song.mp3", DurationMs = 1000 },
                        Contributions = new List<SyncPlaybackContribution>()
                    },
                    new SyncPlaybackSong
                    {
                        Song = new SyncPlaybackSongIdentity { FileName = "Approx.mp3", NormalizedFileName = "approx.mp3", DurationMs = 1000 },
                        Contributions = new List<SyncPlaybackContribution>()
                    }
                },
                Tombstones = new List<SyncPlaybackTombstone>()
            });

            var songs = _repository.LoadState().Songs.OrderBy(x => x.NormalizedFileName).ToList();
            Assert.That(songs.Single(x => x.NormalizedFileName == "song.mp3").LocalTrackId, Is.Not.Null);
            Assert.That(songs.Single(x => x.NormalizedFileName == "approx.mp3").LocalTrackId, Is.Not.Null);
        }

        [Test]
        public void Relink_ExactSharesTrackAndPreservesBothContributionsOnExport()
        {
            var trackId = InsertTrack("precedence.mp3", 1000);
            var fuzzy = EnsureSong("precedence.mp3", 900); var exact = EnsureSong("precedence.mp3", 1000);
            using (var connection = _database.GetConnection()) connection.Execute("UPDATE PlaybackSyncSongs SET LocalTrackId=@trackId WHERE SongId=@songId", new { trackId, songId = fuzzy.SongId });
            Register("device-a");
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = fuzzy.SongId, DeviceId = "device-a", Generation = 0, UndatedListenMs = 3, UpdatedAtUtcMs = 3, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-01", ListenMs = 4 } } });
            _repository.MergeContribution(new PlaybackSyncContribution { SongId = exact.SongId, DeviceId = "device-a", Generation = 0, UndatedListenMs = 8, UpdatedAtUtcMs = 8, DailyBuckets = { new PlaybackSyncDailyBucket { LocalDate = "2026-03-02", ListenMs = 9 } } });

            Assert.That(_adapter.RelinkExactAndUniqueFuzzy(), Is.EqualTo(1));
            var state = _repository.LoadState();
            Assert.That(state.Songs.Single(x => x.SongId == exact.SongId).LocalTrackId, Is.EqualTo(trackId));
            Assert.That(state.Songs.Single(x => x.SongId == fuzzy.SongId).LocalTrackId, Is.EqualTo(trackId));
            Assert.That(state.Contributions.Single(x => x.SongId == fuzzy.SongId).UndatedListenMs, Is.EqualTo(3));
            Assert.That(state.Contributions.Single(x => x.SongId == exact.SongId).UndatedListenMs, Is.EqualTo(8));
            Assert.That(_adapter.RelinkExactAndUniqueFuzzy(), Is.EqualTo(0));

            using (var connection = _database.GetConnection())
            {
                var exported = _adapter.Export(connection);
                Assert.That(exported.Songs, Has.Count.EqualTo(2));
                Assert.That(exported.Songs.Single(x => x.Song.DurationMs == 900).Contributions.Single().UndatedListenMs, Is.EqualTo(3));
                Assert.That(exported.Songs.Single(x => x.Song.DurationMs == 1000).Contributions.Single().UndatedListenMs, Is.EqualTo(8));
            }
        }

        private void ApplyStatisticsSnapshot(SyncPlaybackStatistics statistics)
        {
            using (var connection = _database.GetConnection())
            using (var transaction = connection.BeginTransaction())
            {
                _adapter.Apply(connection, transaction, statistics);
                transaction.Commit();
            }

            _adapter.RelinkExactAndUniqueFuzzy();
        }

        private PlaybackSyncSong EnsureSong(string fileName, long durationMs)
        {
            return _repository.EnsureSong(new PlaybackSyncSong { FileName = fileName, DurationMs = durationMs });
        }

        private void Register(string deviceId, string platform = "windows")
        {
            _repository.EnsureDevice(new PlaybackSyncDevice { DeviceId = deviceId, CurrentGeneration = 0, DisplayName = deviceId, DisplayNameUpdatedAtUtcMs = 1, Platform = platform, FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 });
        }

        private static SyncPlaybackDevice Device(string deviceId, string platform)
        {
            return new SyncPlaybackDevice { DeviceId = deviceId, CurrentGeneration = 0, DisplayName = deviceId, DisplayNameUpdatedAtUtcMs = 1, Platform = platform, FirstSeenAtUtcMs = 1, LastSeenAtUtcMs = 1 };
        }

        private int InsertTrack(string fileName, long durationMs)
        {
            using (var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                connection.Open();
                return connection.ExecuteScalar<int>("INSERT INTO Tracks (FileName, FilePath, TotalSamples, DurationMs) VALUES (@FileName, @FilePath, 1, @DurationMs); SELECT last_insert_rowid();", new { FileName = fileName, FilePath = @"C:\Music\" + fileName, DurationMs = durationMs });
            }
        }

        private static string SerializeStatistics(SyncPlaybackStatistics statistics)
        {
            return SyncSnapshotSerializer.Serialize(new SyncSnapshot { SchemaVersion = 2, DeviceId = "test", ExportedAt = 1, Playlists = new List<SyncPlaylist>(), LoopPoints = new List<SyncLoopPointEntry>(), Ratings = new List<SyncRatingEntry>(), PlaybackStatistics = statistics });
        }

        private static string AndroidGoldenFixtureJson()
        {
            return @"{
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
        }
    }
}
