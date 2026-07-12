using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class GitHubSyncPreparationServiceTests
    {
        private string _dbPath;
        private DatabaseHelper _db;
        private SQLiteSyncSnapshotStore _store;
        private PlaybackStatisticsLocalService _localStatistics;
        private PlaybackServiceTestDouble _playback;
        private GitHubSyncPreparationService _preparation;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"PreparationTest_{Guid.NewGuid()}.db");
            _db = new DatabaseHelper(_dbPath);
            _db.InitializeDatabase();
            _store = new SQLiteSyncSnapshotStore(_db);
            _localStatistics = new PlaybackStatisticsLocalService(_db, new PlaybackStatisticsSyncRepository(_dbPath));
            _playback = new PlaybackServiceTestDouble();
            _preparation = new GitHubSyncPreparationService(_store, _playback);
        }

        [TearDown]
        public void TearDown()
        {
            _playback?.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        [Test]
        public async Task CaptureFreshLocalSnapshot_UsesCheckpointAndExportsCanonicalV2()
        {
            var snapshot = await _preparation.CaptureFreshLocalSnapshotAsync();

            Assert.That(_playback.CheckpointCount, Is.EqualTo(1));
            Assert.That(snapshot.SchemaVersion, Is.EqualTo(2));
            Assert.That(snapshot.PlaybackStatistics, Is.Not.Null);
            Assert.That(snapshot.PlaybackStatistics.DateBucketBasis, Is.EqualTo("sourceLocal"));
            Assert.That(snapshot.PlaybackStatistics.Devices, Is.Empty);
            Assert.DoesNotThrow(() => SyncSnapshotSerializer.Serialize(snapshot));
        }

        [Test]
        public async Task ForcePush_V2PreservesLocalDomainsMergesRemoteStatsAndRotatesTombstonedGeneration()
        {
            _db.SetSetting("Sync.DeviceId", "local-device");
            var localContext = _localStatistics.GetRecordingContext();
            await _localStatistics.ApplyAsync(new PlaybackStatisticsSettlement
            {
                SettlementEventId = "local-settlement",
                FileName = "local.mp3",
                NormalizedFileName = "local.mp3",
                TrackDurationMs = 1000,
                DeviceId = localContext.DeviceId,
                Generation = localContext.CurrentGeneration,
                StartedAtUtcMs = 10,
                DurationMs = 100,
                SourceLocalDate = "2026-07-10",
                AppliedAtUtcMs = 20,
                SourceKind = "test"
            });

            SeedLocalPlaylist();
            SeedRating(1, 4, DateTime.Now);
            var remote = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote-device",
                ExportedAt = 2,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist { Id = "remote-playlist", Name = "Remote", CreatedAt = 2, ModifiedAt = 2, Items = new List<SyncPlaylistItem>() }
                },
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "local.mp3", DurationMs = 1000 },
                        Rating = new SyncRating { RatingValue = 1, LastModified = 3 }
                    }
                },
                PlaybackStatistics = new SyncPlaybackStatistics
                {
                    DateBucketBasis = "sourceLocal",
                    Devices = new List<SyncPlaybackDevice>
                    {
                        Device(localContext.DeviceId, "windows", 0),
                        Device("remote-device", "android", 0)
                    },
                    Songs = new List<SyncPlaybackSong>
                    {
                        new SyncPlaybackSong
                        {
                            Song = new SyncPlaybackSongIdentity { FileName = "unresolved.mp3", NormalizedFileName = "unresolved.mp3", DurationMs = 2000 },
                            Contributions = new List<SyncPlaybackContribution>
                            {
                                Contribution("remote-device", 0, 300)
                            }
                        }
                    },
                    Tombstones = new List<SyncPlaybackTombstone>
                    {
                        new SyncPlaybackTombstone
                        {
                            DeviceId = localContext.DeviceId,
                            Generation = localContext.CurrentGeneration,
                            Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope,
                            TombstonedAtUtcMs = 3,
                            TombstonedByDeviceId = "remote-device",
                            Reason = "remote-reset"
                        }
                    }
                }
            };

            _playback.RotateFunc = () => _localStatistics.ObserveCurrentGenerationTombstoneAsync()
                .ContinueWith(task => task.Result.Rotated);

            var prepared = await _preparation.PrepareForcePushAsync(remote);

            Assert.That(_playback.CheckpointCount, Is.EqualTo(2));
            Assert.That(_playback.RotateCount, Is.EqualTo(1));
            Assert.That(prepared.Outbound.Playlists.Any(x => x.Name == "Local"), Is.True);
            Assert.That(prepared.Outbound.Playlists.Any(x => x.Name == "Remote"), Is.False);
            Assert.That(prepared.Outbound.Ratings.Single(x => x.Song.FileName == "local.mp3").Rating.RatingValue, Is.EqualTo(4));
            Assert.That(prepared.Outbound.PlaybackStatistics.Songs.Any(x => x.Song.NormalizedFileName == "unresolved.mp3"), Is.True);
            Assert.That(prepared.Outbound.PlaybackStatistics.Songs.SelectMany(x => x.Contributions)
                .Any(x => x.DeviceId == "remote-device" && x.Generation == 0 &&
                    x.DatedListenMs["2026-07-10"] == 300), Is.True);
            Assert.That(prepared.Outbound.PlaybackStatistics.Tombstones.Any(x =>
                x.DeviceId == localContext.DeviceId && x.Generation == localContext.CurrentGeneration &&
                x.Reason == "remote-reset"), Is.True);
            Assert.That(prepared.Outbound.PlaybackStatistics.Devices.Single(x => x.DeviceId == localContext.DeviceId).CurrentGeneration, Is.GreaterThan(localContext.CurrentGeneration));
            Assert.That(prepared.Outbound.PlaybackStatistics.Songs
                .SelectMany(x => x.Contributions)
                .Any(x => x.DeviceId == localContext.DeviceId && x.Generation == localContext.CurrentGeneration), Is.False);
            Assert.DoesNotThrow(() => SyncSnapshotSerializer.Serialize(prepared.Outbound));
        }

        [Test]
        public void ForcePush_RejectsNonV2RemoteBeforeTreatingItAsAbsent()
        {
            var remote = new SyncSnapshot { SchemaVersion = 1, DeviceId = "legacy", ExportedAt = 1 };

            Assert.Throws<FormatException>(() => _preparation.PrepareForcePushAsync(remote).GetAwaiter().GetResult());
            Assert.That(_playback.CheckpointCount, Is.EqualTo(0));
        }

        [Test]
        public async Task NormalSync_ReexportsAfterApplyAndRotation()
        {
            var remote = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 2,
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };

            var prepared = await _preparation.PrepareNormalSyncAsync(remote);

            Assert.That(_playback.CheckpointCount, Is.EqualTo(2));
            Assert.That(_playback.RotateCount, Is.EqualTo(1));
            Assert.That(prepared.Outbound.SchemaVersion, Is.EqualTo(2));
            Assert.DoesNotThrow(() => SyncSnapshotSerializer.Serialize(prepared.Outbound));
        }

        [Test]
        public async Task Coordinator_WithProductionPreparation_AppliesRotatesReexportsAndUploadsCanonicalV2()
        {
            _db.SetSetting("Sync.DeviceId", "local-device");
            var localContext = _localStatistics.GetRecordingContext();
            await _localStatistics.ApplyAsync(new PlaybackStatisticsSettlement
            {
                SettlementEventId = "local-settlement",
                FileName = "local.mp3",
                NormalizedFileName = "local.mp3",
                TrackDurationMs = 1000,
                DeviceId = localContext.DeviceId,
                Generation = localContext.CurrentGeneration,
                StartedAtUtcMs = 10,
                DurationMs = 100,
                SourceLocalDate = "2026-07-10",
                AppliedAtUtcMs = 20,
                SourceKind = "test"
            });

            var remote = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote-device",
                ExportedAt = 2,
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = new SyncPlaybackStatistics
                {
                    DateBucketBasis = "sourceLocal",
                    Devices = new List<SyncPlaybackDevice>
                    {
                        Device(localContext.DeviceId, "windows", localContext.CurrentGeneration),
                        Device("remote-device", "android", 0)
                    },
                    Songs = new List<SyncPlaybackSong>
                    {
                        new SyncPlaybackSong
                        {
                            Song = new SyncPlaybackSongIdentity
                            {
                                FileName = "unresolved.mp3",
                                NormalizedFileName = "unresolved.mp3",
                                DurationMs = 2000
                            },
                            Contributions = new List<SyncPlaybackContribution>
                            {
                                Contribution("remote-device", 0, 300)
                            }
                        }
                    },
                    Tombstones = new List<SyncPlaybackTombstone>
                    {
                        new SyncPlaybackTombstone
                        {
                            DeviceId = localContext.DeviceId,
                            Generation = localContext.CurrentGeneration,
                            Scope = SyncSnapshotSerializer.DeviceGenerationTombstoneScope,
                            TombstonedAtUtcMs = 3,
                            TombstonedByDeviceId = "remote-device",
                            Reason = "source-local"
                        }
                    }
                }
            };

            _playback.RotateFunc = () => _localStatistics.ObserveCurrentGenerationTombstoneAsync()
                .ContinueWith(task => task.Result.Rotated);
            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot { Exists = true, Revision = "remote-sha", Snapshot = remote },
                UploadResult = "uploaded-sha"
            };
            var coordinator = new GitHubSyncCoordinator(backend, _db, _preparation);

            var report = await coordinator.SyncNowAsync(new GitHubSyncConfig
            {
                Owner = "owner", Repository = "repo", Branch = "main", Path = "sync.json", Token = "token"
            });

            Assert.That(report.Success, Is.True);
            Assert.That(report.ApplyResult, Is.Not.Null);
            Assert.That(backend.LastUploadedSnapshot, Is.Not.Null);
            Assert.That(_playback.CheckpointCount, Is.EqualTo(2));
            Assert.That(_playback.RotateCount, Is.EqualTo(1));
            Assert.That(_playback.Lifecycle.IndexOf("rotate"), Is.LessThan(_playback.Lifecycle.LastIndexOf("capture")));
            Assert.That(backend.LastUploadedSnapshot.PlaybackStatistics.Songs.Any(x => x.Song.NormalizedFileName == "unresolved.mp3"), Is.True);
            Assert.That(backend.LastUploadedSnapshot.PlaybackStatistics.Tombstones.Any(x =>
                x.DeviceId == localContext.DeviceId && x.Generation == localContext.CurrentGeneration), Is.True);
            Assert.That(backend.LastUploadedSnapshot.PlaybackStatistics.Devices.Single(x => x.DeviceId == localContext.DeviceId).CurrentGeneration,
                Is.GreaterThan(localContext.CurrentGeneration));
            Assert.That(backend.LastUploadedSnapshot.PlaybackStatistics.Songs.SelectMany(x => x.Contributions).Any(x =>
                x.DeviceId == localContext.DeviceId && x.Generation == localContext.CurrentGeneration), Is.False);
            Assert.DoesNotThrow(() => SyncSnapshotSerializer.Serialize(backend.LastUploadedSnapshot));
        }

        [Test]
        public void CheckpointFailurePreventsLocalCapture()
        {
            _playback.FailCheckpoint = true;

            Assert.ThrowsAsync<InvalidOperationException>(() => _preparation.CaptureFreshLocalSnapshotAsync());

            _playback.FailCheckpoint = false;
            Assert.That(_preparation.CaptureFreshLocalSnapshotAsync().Result.SchemaVersion, Is.EqualTo(2));
        }

        private void SeedLocalPlaylist()
        {
            using (var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                connection.Open();
                connection.Execute("INSERT INTO Tracks (Id, FileName, FilePath, TotalSamples, DurationMs, LastModified) VALUES (1,'local.mp3','local.mp3',1000,1000,@Now)", new { Now = DateTime.Now });
                var playlistId = connection.ExecuteScalar<int>("INSERT INTO Playlists (Name) VALUES ('Local'); SELECT last_insert_rowid();");
                connection.Execute("INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@playlistId,1,0)", new { playlistId });
            }
        }

        private void SeedRating(int trackId, int rating, DateTime lastModified)
        {
            using (var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                connection.Open();
                connection.Execute(@"
                    INSERT INTO UserRatings (TrackId, Rating, LastModified)
                    VALUES (@TrackId, @Rating, @LastModified)",
                    new { TrackId = trackId, Rating = rating, LastModified = lastModified });
            }
        }

        private static SyncPlaybackDevice Device(string id, string platform, long generation)
        {
            return new SyncPlaybackDevice
            {
                DeviceId = id,
                Platform = platform,
                CurrentGeneration = generation,
                DisplayName = id,
                FirstSeenAtUtcMs = 1,
                LastSeenAtUtcMs = 2,
                DisplayNameUpdatedAtUtcMs = 1
            };
        }

        private static SyncPlaybackContribution Contribution(string deviceId, long generation, long listenMs)
        {
            return new SyncPlaybackContribution
            {
                DeviceId = deviceId,
                Generation = generation,
                DatedListenMs = new Dictionary<string, long> { ["2026-07-10"] = listenMs },
                UndatedListenMs = 0,
                UpdatedAtUtcMs = 2
            };
        }

        private static SyncSnapshot Clone(SyncSnapshot snapshot)
        {
            return SyncSnapshotSerializer.Deserialize(SyncSnapshotSerializer.Serialize(snapshot));
        }
    }
}
