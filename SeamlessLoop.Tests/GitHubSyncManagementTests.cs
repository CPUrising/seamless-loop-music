using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Backend;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class GitHubSyncManagementTests
    {
        private string _dbPath;
        private DatabaseHelper _dbHelper;
        private SQLiteSyncSnapshotStore _store;
        private GitHubSyncManagementService _mgt;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"MgtTest_{Guid.NewGuid()}.db");
            _dbHelper = new DatabaseHelper(_dbPath);
            _dbHelper.InitializeDatabase();
            _store = new SQLiteSyncSnapshotStore(_dbHelper);

            // Save a valid config so management service can use it
            _dbHelper.SetSetting("Sync.GitHub.Owner", "owner");
            _dbHelper.SetSetting("Sync.GitHub.Repository", "repo");
            _dbHelper.SetSetting("Sync.GitHub.Branch", "main");
            _dbHelper.SetSetting("Sync.GitHub.Path", "sync.json");
            _dbHelper.SetSetting("Sync.GitHub.Token", "ghp_token");

            _mgt = new GitHubSyncManagementService(_dbHelper, _store, new FakeSyncBackend());
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

        // ────────────────────────────────────────────────
        //  Overview: local counts
        // ────────────────────────────────────────────────

        [Test]
        public void Overview_LocalCounts_ExcludeDefaultFullTrackLoop()
        {
            // Track with a real substantial loop
            SeedTrack(1, "real_loop.mp3", 441000, 20000);
            SeedLoopPoint(1, 5000, 400000, DateTime.Now.AddDays(-1));

            // Track with default full-track loop (0→TotalSamples)
            SeedTrack(2, "default_loop.mp3", 882000, 40000);
            SeedLoopPoint(2, 0, 882000, DateTime.Now);

            // Track with 0/0 (mobile unset)
            SeedTrack(3, "unset.mp3", 100000, 5000);
            SeedLoopPoint(3, 0, 0, DateTime.Now);

            // Track with rating
            SeedTrack(4, "rated.mp3", 200000, 10000);
            SeedRating(4, 4, DateTime.Now);

            // Playlist
            SeedPlaylist("Test PL");
            SeedPlaylistItem(1, 1, 0);

            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot { Exists = false }
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, backend);

            var overview = _mgt.RefreshOverviewAsync().Result;

            // Local counts
            Assert.That(overview.Local.SongCount, Is.EqualTo(4));
            Assert.That(overview.Local.PlaylistCount, Is.EqualTo(1));
            Assert.That(overview.Local.LoopPointCount, Is.EqualTo(1), "Only the real loop should count");
            Assert.That(overview.Local.RatingCount, Is.EqualTo(1));
        }

        // ────────────────────────────────────────────────
        //  Overview: cloud counts and refs
        // ────────────────────────────────────────────────

        [Test]
        public void Overview_CloudCountsAndRefs()
        {
            SeedTrack(1, "song_a.mp3", 441000, 20000);
            SeedTrack(2, "song_b.mp3", 882000, 40000);

            var remoteSnap = new SyncSnapshot
            {
                SchemaVersion = 1,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song_a.mp3", DurationMs = 20000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 5000, LastModified = 100 }
                    }
                },
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song_b.mp3", DurationMs = 40000 },
                        Rating = new SyncRating { RatingValue = 5, LastModified = 200 }
                    }
                },
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Cloud PL", CreatedAt = 100, ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "song_a.mp3", DurationMs = 20000 },
                                SortOrder = 0
                            },
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "new_song.mp3", DurationMs = 50000 },
                                SortOrder = 1
                            }
                        }
                    }
                }
            };

            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnap,
                    Revision = "sha123"
                }
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, backend);

            var overview = _mgt.RefreshOverviewAsync().Result;

            Assert.That(overview.CloudExists, Is.True);
            Assert.That(overview.Cloud.PlaylistCount, Is.EqualTo(1));
            Assert.That(overview.Cloud.LoopPointCount, Is.EqualTo(1));
            Assert.That(overview.Cloud.RatingCount, Is.EqualTo(1));
            // 3 distinct refs: song_a (lp+playlist), song_b (rating), new_song (playlist)
            Assert.That(overview.Cloud.SongReferenceCount, Is.EqualTo(3));
        }

        // ────────────────────────────────────────────────
        //  Overview: matched / missing cloud refs
        // ────────────────────────────────────────────────

        [Test]
        public void Overview_MatchedAndMissingCloudRefs()
        {
            // Local tracks: song_a matched, song_b matched, missing_song does not exist locally
            SeedTrack(1, "song_a.mp3", 441000, 20000);
            SeedTrack(2, "song_b.mp3", 882000, 40000);

            var remoteSnap = new SyncSnapshot
            {
                SchemaVersion = 1,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song_a.mp3", DurationMs = 20000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 5000, LastModified = 100 }
                    },
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song_b.mp3", DurationMs = 40000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 200, LoopEnd = 8000, LastModified = 150 }
                    },
                    // This song does not exist locally
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "missing_song.flac", DurationMs = 60000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 300, LoopEnd = 9000, LastModified = 200 }
                    }
                }
            };

            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnap,
                    Revision = "sha123"
                }
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, backend);

            var overview = _mgt.RefreshOverviewAsync().Result;

            Assert.That(overview.MatchedCloudSongReferences, Is.EqualTo(2),
                "song_a and song_b should match");
            Assert.That(overview.MissingCloudSongReferences, Is.EqualTo(1),
                "missing_song.flac should not match");
        }

        [Test]
        public void Overview_AmbiguousRef_CountedAsMissing()
        {
            // Two local tracks with same name, same duration — ambiguous
            SeedTrack(1, "ambiguous.mp3", 100000, 5000);
            SeedTrack(2, "ambiguous.mp3", 200000, 5000);

            var remoteSnap = new SyncSnapshot
            {
                SchemaVersion = 1,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "ambiguous.mp3", DurationMs = 5000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 5000, LastModified = 100 }
                    }
                }
            };

            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnap,
                    Revision = "sha123"
                }
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, backend);

            var overview = _mgt.RefreshOverviewAsync().Result;

            Assert.That(overview.MatchedCloudSongReferences, Is.EqualTo(0),
                "Ambiguous song should not be matched");
            Assert.That(overview.MissingCloudSongReferences, Is.EqualTo(1),
                "Ambiguous song should count as missing");
        }

        // ────────────────────────────────────────────────
        //  Force push: uploads local snapshot with sha
        // ────────────────────────────────────────────────

        [Test]
        public void Overview_MicroDiffTotalSamples_SameDuration_MatchedNotMissing()
        {
            // Cloud reference has totalSamples=10583412 (phone), local track has 10583426 (desktop)
            // Same fileName and DurationMs=239987 → should match as same song
            SeedTrack(1, "Summer Pockets.flac", 10583426, 239987);

            var remoteSnap = new SyncSnapshot
            {
                SchemaVersion = 1,
                DeviceId = "phone",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity
                        {
                            FileName = "Summer Pockets.flac",
                            DurationMs = 239987,
                            TotalSamples = 10583412 // phone, differs by 14
                        },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 5000, LastModified = 100 }
                    }
                }
            };

            var backend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = remoteSnap,
                    Revision = "sha123"
                }
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, backend);

            var overview = _mgt.RefreshOverviewAsync().Result;

            Assert.That(overview.CloudExists, Is.True);
            Assert.That(overview.Cloud.SongReferenceCount, Is.EqualTo(1));
            Assert.That(overview.MatchedCloudSongReferences, Is.EqualTo(1),
                "Same fileName+durationMs should match despite totalSamples micro-diff");
            Assert.That(overview.MissingCloudSongReferences, Is.EqualTo(0));
        }

        [Test]
        public void ForcePush_DownloadsRevisionThenUploadsLocal()
        {
            SeedTrack(1, "song.mp3", 441000, 20000);

            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot
                {
                    Exists = true,
                    Snapshot = new SyncSnapshot
                    {
                        SchemaVersion = 1,
                        DeviceId = "remote",
                        ExportedAt = 1000
                    },
                    Revision = "remote_sha"
                },
                UploadResult = "new_sha_after_push"
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, fakeBackend);

            var result = _mgt.ForcePushLocalToCloudAsync().Result;

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo("uploaded"));
            Assert.That(result.Revision, Is.EqualTo("new_sha_after_push"));

            // Should have used remote SHA as expectedRevision
            Assert.That(fakeBackend.LastExpectedRevision, Is.EqualTo("remote_sha"));

            // Should have saved metadata
            var savedSha = _dbHelper.GetSetting("Sync.GitHub.LastRemoteSha");
            Assert.That(savedSha, Is.EqualTo("new_sha_after_push"));

            var savedTime = _dbHelper.GetSetting("Sync.GitHub.LastSyncTime");
            Assert.That(savedTime, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ForcePush_RemoteMissing_UploadsWithoutSha()
        {
            SeedTrack(1, "song.mp3", 441000, 20000);

            var fakeBackend = new FakeSyncBackend
            {
                DownloadResult = new RemoteSyncSnapshot { Exists = false },
                UploadResult = "new_sha"
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, fakeBackend);

            var result = _mgt.ForcePushLocalToCloudAsync().Result;

            Assert.That(result.Success, Is.True);
            // expectedRevision should be null when remote doesn't exist
            Assert.That(fakeBackend.LastExpectedRevision, Is.Null);
        }

        [Test]
        public void ForcePush_Unauthorized_ReturnsFailure()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DownloadFunc = async (config, ct) =>
                    throw new SyncBackendException(SyncBackendCode.Unauthorized, "bad token")
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, fakeBackend);

            var result = _mgt.ForcePushLocalToCloudAsync().Result;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("unauthorized"));
        }

        // ────────────────────────────────────────────────
        //  Delete cloud
        // ────────────────────────────────────────────────

        [Test]
        public void DeleteCloud_CallsBackendAndClearsMetadata()
        {
            // Save previous metadata
            _dbHelper.SetSetting("Sync.GitHub.LastRemoteSha", "old_sha");
            _dbHelper.SetSetting("Sync.GitHub.LastSyncTime", "12345");

            var fakeBackend = new FakeSyncBackend();
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, fakeBackend);

            var result = _mgt.DeleteCloudSnapshotAsync().Result;

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo("deleted"));
            Assert.That(fakeBackend.DeleteCalled, Is.True);

            // Metadata should be cleared
            Assert.That(_dbHelper.GetSetting("Sync.GitHub.LastRemoteSha"), Is.EqualTo(""));
            Assert.That(_dbHelper.GetSetting("Sync.GitHub.LastSyncTime"), Is.EqualTo(""));
        }

        [Test]
        public void DeleteCloud_RemoteMissing_TreatedAsSuccess()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DeleteFunc = async (config, ct) => { } // simulates DELETE that doesn't throw
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, fakeBackend);

            var result = _mgt.DeleteCloudSnapshotAsync().Result;
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void DeleteCloud_Unauthorized_ReturnsFailure()
        {
            var fakeBackend = new FakeSyncBackend
            {
                DeleteFunc = async (config, ct) =>
                    throw new SyncBackendException(SyncBackendCode.Unauthorized, "bad token")
            };
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, fakeBackend);

            var result = _mgt.DeleteCloudSnapshotAsync().Result;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("unauthorized"));
        }

        // ────────────────────────────────────────────────
        //  Clear local sync data
        // ────────────────────────────────────────────────

        [Test]
        public void ClearLocal_Selective_OnlyClearsChosen()
        {
            SeedTrack(1, "keep.mp3", 441000, 20000);
            SeedLoopPoint(1, 100, 400000, DateTime.Now);
            SeedRating(1, 4, DateTime.Now);
            int plId = SeedPlaylist("ToKeep");
            SeedPlaylistItem(plId, 1, 0);

            // Also seed a playlist sync-id mapping
            _dbHelper.SetSetting("Sync.PlaylistId.1", "some-uuid");
            _dbHelper.SetSetting("Sync.PlaylistLocalId.some-uuid", "1");

            var selection = new ClearLocalSyncDataSelection
            {
                ClearPlaylists = true,
                ClearLoopPoints = false,
                ClearRatings = true
            };

            var result = _mgt.ClearLocalSyncDataAsync(selection).Result;

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo("cleared"));
            Assert.That(result.AffectedCount, Is.GreaterThan(0));

            // Verify: loop points should survive, playlists and ratings should be gone
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Tracks"), Is.EqualTo(1),
                    "Tracks should not be deleted");
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Playlists"), Is.EqualTo(0),
                    "Playlists should be cleared");
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaylistItems"), Is.EqualTo(0),
                    "PlaylistItems should be cleared");
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM LoopPoints"), Is.EqualTo(1),
                    "LoopPoints should survive");
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UserRatings"), Is.EqualTo(0),
                    "Ratings should be cleared");
            }

            // Playlist mappings should be cleared
            Assert.That(_dbHelper.GetSetting("Sync.PlaylistId.1"), Is.Null,
                "Playlist ID mapping should be cleared");
            Assert.That(_dbHelper.GetSetting("Sync.GitHub.LastRemoteSha"), Is.EqualTo(""),
                "LastRemoteSha should be cleared");
        }

        [Test]
        public void ClearLocal_NoSelection_ReturnsFailure()
        {
            var selection = new ClearLocalSyncDataSelection
            {
                ClearPlaylists = false,
                ClearLoopPoints = false,
                ClearRatings = false
            };

            var result = _mgt.ClearLocalSyncDataAsync(selection).Result;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("no_selection"));
        }

        [Test]
        public void ClearLocal_NullSelection_ReturnsFailure()
        {
            var result = _mgt.ClearLocalSyncDataAsync(null).Result;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("no_selection"));
        }

        [Test]
        public void ClearLocal_LoopPointsOnly()
        {
            SeedTrack(1, "keep.mp3", 441000, 20000);
            SeedLoopPoint(1, 100, 400000, DateTime.Now);
            SeedRating(1, 4, DateTime.Now);

            var selection = new ClearLocalSyncDataSelection
            {
                ClearPlaylists = false,
                ClearLoopPoints = true,
                ClearRatings = false
            };

            var result = _mgt.ClearLocalSyncDataAsync(selection).Result;
            Assert.That(result.Success, Is.True);

            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM LoopPoints"), Is.EqualTo(0));
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UserRatings"), Is.EqualTo(1),
                    "Ratings should survive");
                Assert.That(conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Tracks"), Is.EqualTo(1));
            }
        }

        // ────────────────────────────────────────────────
        //  Config missing
        // ────────────────────────────────────────────────

        [Test]
        public void Overview_ConfigMissing_ReturnsNotConfigured()
        {
            // Remove config settings
            _dbHelper.SetSetting("Sync.GitHub.Owner", "");
            _dbHelper.SetSetting("Sync.GitHub.Token", "");

            // Recreate management service (uses current config from DB)
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, new FakeSyncBackend());

            var overview = _mgt.RefreshOverviewAsync().Result;

            Assert.That(overview.Status, Is.EqualTo("not_configured"));
            Assert.That(overview.CloudExists, Is.False);
            Assert.That(overview.Local.SongCount, Is.EqualTo(0));
        }

        [Test]
        public void ForcePush_ConfigMissing_ReturnsFailure()
        {
            // Remove config
            _dbHelper.SetSetting("Sync.GitHub.Owner", "");
            _dbHelper.SetSetting("Sync.GitHub.Token", "");
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, new FakeSyncBackend());

            var result = _mgt.ForcePushLocalToCloudAsync().Result;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("not_configured"));
        }

        [Test]
        public void DeleteCloud_ConfigMissing_ReturnsFailure()
        {
            _dbHelper.SetSetting("Sync.GitHub.Owner", "");
            _dbHelper.SetSetting("Sync.GitHub.Token", "");
            _mgt = new GitHubSyncManagementService(_dbHelper, _store, new FakeSyncBackend());

            var result = _mgt.DeleteCloudSnapshotAsync().Result;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("not_configured"));
        }

        // ────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────

        private void SeedTrack(int id, string fileName, long totalSamples, long durationMs)
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute(@"
                    INSERT OR REPLACE INTO Tracks (Id, FileName, FilePath, TotalSamples, DurationMs, LastModified)
                    VALUES (@Id, @Fn, @Fp, @Ts, @Dms, @Now)",
                    new { Id = id, Fn = fileName, Fp = @"C:\Music\" + fileName, Ts = totalSamples, Dms = durationMs, Now = DateTime.Now });
            }
        }

        private void SeedLoopPoint(int trackId, long loopStart, long loopEnd, DateTime? lastModified = null)
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute(@"
                    INSERT OR REPLACE INTO LoopPoints (TrackId, LoopStart, LoopEnd, AnalysisLastModified)
                    VALUES (@Id, @Start, @End, @Mod)",
                    new { Id = trackId, Start = loopStart, End = loopEnd, Mod = lastModified ?? DateTime.Now });
            }
        }

        private void SeedRating(int trackId, int rating, DateTime? lastModified = null)
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute(@"
                    INSERT OR REPLACE INTO UserRatings (TrackId, Rating, LastModified)
                    VALUES (@Id, @Rating, @Mod)",
                    new { Id = trackId, Rating = rating, Mod = lastModified ?? DateTime.Now });
            }
        }

        private int SeedPlaylist(string name)
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(
                    "INSERT INTO Playlists (Name) VALUES (@Name); SELECT last_insert_rowid();",
                    new { Name = name });
            }
        }

        private void SeedPlaylistItem(int playlistId, int songId, int sortOrder)
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute(
                    "INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@Pid, @Sid, @Order)",
                    new { Pid = playlistId, Sid = songId, Order = sortOrder });
            }
        }
    }
}
