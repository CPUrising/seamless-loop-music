using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using NUnit.Framework;
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Models;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class SyncCoreTests
    {
        // ────────────────────────────────────────────────────────
        //  Serializer tests
        // ────────────────────────────────────────────────────────

        [Test]
        public void Serializer_RejectsLegacySchemaV1()
        {
            var json = @"{
                ""schemaVersion"": 1,
                ""deviceId"": ""abc-123"",
                ""exportedAt"": 1700000000000
            }";

            var exception = Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Deserialize(json));
            Assert.That(exception.Message, Does.Contain("Expected: 2"));
        }

        [Test]
        public void Serializer_RejectsLegacyMobileShape()
        {
            var json = @"{
                ""schemaVersion"": 1,
                ""deviceId"": ""3f76252c-2c35-4951-902e-55bfaee18e07"",
                ""exportedAt"": 1783427596608,
                ""loopPoints"": [
                    {
                        ""loopPoint"": { ""lastModified"": 1783427596608, ""loopEnd"": 10583412, ""loopStart"": 3368943 },
                        ""song"": { ""durationMs"": 239987, ""fileName"": ""1-02. Summer Pockets.flac"", ""totalSamples"": 10583412 }
                    }
                ],
                ""playlists"": [
                    {
                        ""createdAt"": 1783427577162,
                        ""id"": ""8455d95d-59e1-438e-9269-42573cb80fef"",
                        ""items"": [
                            { ""song"": { ""durationMs"": 239987, ""fileName"": ""1-02. Summer Pockets.flac"", ""totalSamples"": 10583412 }, ""sortOrder"": 0 }
                        ],
                        ""modifiedAt"": 1783427596608,
                        ""name"": ""3""
                    }
                ],
                ""ratings"": [
                    {
                        ""rating"": { ""lastModified"": 1783343165165, ""rating"": 4 },
                        ""song"": { ""durationMs"": 239987, ""fileName"": ""1-02. Summer Pockets.flac"", ""totalSamples"": 10583412 }
                    }
                ]
            }";

            Assert.Throws<FormatException>(() => SyncSnapshotSerializer.Deserialize(json));
        }

        [Test]
        public void Serializer_RejectsWrongSchemaVersion()
        {
            var ex0 = Assert.Throws<FormatException>(() =>
                SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":0,""deviceId"":""x"",""exportedAt"":1}"));
            Assert.That(ex0.Message, Does.Contain("schemaVersion"));

            var ex1 = Assert.Throws<FormatException>(() =>
                SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":1,""deviceId"":""x"",""exportedAt"":1}"));
            Assert.That(ex1.Message, Does.Contain("Expected: 2"));

            var ex2 = Assert.Throws<FormatException>(() =>
                SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":3,""deviceId"":""x"",""exportedAt"":1}"));
            Assert.That(ex2.Message, Does.Contain("Expected: 2"));
        }

        [Test]
        public void Serializer_NullListsBecomeEmpty()
        {
            var json = @"{""schemaVersion"":2,""deviceId"":""d"",""exportedAt"":0,""playbackStatistics"":{""dateBucketBasis"":""sourceLocal"",""devices"":[],""songs"":[],""tombstones"":[]}}";
            var snap = SyncSnapshotSerializer.Deserialize(json);

            Assert.That(snap.Playlists, Is.Not.Null);
            Assert.That(snap.LoopPoints, Is.Not.Null);
            Assert.That(snap.Ratings, Is.Not.Null);
            Assert.That(snap.Playlists, Is.Empty);
            Assert.That(snap.LoopPoints, Is.Empty);
            Assert.That(snap.Ratings, Is.Empty);
        }

        [Test]
        public void Serializer_RejectsMissingDeviceId()
        {
            var ex = Assert.Throws<FormatException>(() =>
                SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":2,""exportedAt"":0,""playbackStatistics"":{""dateBucketBasis"":""sourceLocal"",""devices"":[],""songs"":[],""tombstones"":[]}}"));
            Assert.That(ex.Message, Does.Contain("deviceId"));
        }

        [Test]
        public void Serializer_RejectsNegativeExportedAt()
        {
            var ex = Assert.Throws<FormatException>(() =>
                SyncSnapshotSerializer.Deserialize(@"{""schemaVersion"":2,""deviceId"":""d"",""exportedAt"":-1,""playbackStatistics"":{""dateBucketBasis"":""sourceLocal"",""devices"":[],""songs"":[],""tombstones"":[]}}"));
            Assert.That(ex.Message, Does.Contain("exportedAt"));
        }

        [Test]
        public void Serializer_ParsesOneLineJson()
        {
            var json = "{\"schemaVersion\":2,\"deviceId\":\"d\",\"exportedAt\":1,\"playbackStatistics\":{\"dateBucketBasis\":\"sourceLocal\",\"devices\":[],\"songs\":[],\"tombstones\":[]}}";
            var snap = SyncSnapshotSerializer.Deserialize(json);
            Assert.That(snap.SchemaVersion, Is.EqualTo(2));
            Assert.That(snap.DeviceId, Is.EqualTo("d"));
        }

        [Test]
        public void Serializer_RoundTripPreservesData()
        {
            var original = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "roundtrip-device",
                ExportedAt = 999888777,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-uuid",
                        Name = "Test PL",
                        CreatedAt = 100,
                        ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity
                                {
                                    FileName = "song.mp3",
                                    DurationMs = 40000,
                                    TotalSamples = 1764000
                                },
                                SortOrder = 0
                            }
                        }
                    }
                },
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "lp.mp3", DurationMs = 30000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 1000, LoopEnd = 5000, LastModified = 300 }
                    }
                },
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "rt.mp3", DurationMs = 25000 },
                        Rating = new SyncRating { RatingValue = 5, LastModified = 400 }
                    }
                },
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };

            var json = SyncSnapshotSerializer.Serialize(original);
            var deserialized = SyncSnapshotSerializer.Deserialize(json);

            Assert.That(deserialized.SchemaVersion, Is.EqualTo(2));
            Assert.That(deserialized.DeviceId, Is.EqualTo("roundtrip-device"));
            Assert.That(deserialized.ExportedAt, Is.EqualTo(999888777));
            Assert.That(deserialized.Playlists, Has.Count.EqualTo(1));
            Assert.That(deserialized.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(deserialized.Ratings, Has.Count.EqualTo(1));

            Assert.That(deserialized.Playlists[0].Id, Is.EqualTo("pl-uuid"));
            Assert.That(deserialized.Playlists[0].Items[0].Song.FileName, Is.EqualTo("song.mp3"));
            Assert.That(deserialized.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(1000));
            Assert.That(deserialized.Ratings[0].Rating.RatingValue, Is.EqualTo(5));
        }

        [Test]
        public void Serializer_SerializesIndented()
        {
            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "d",
                ExportedAt = 0,
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };

            var json = SyncSnapshotSerializer.Serialize(snap);
            // JSON should have newlines (indented)
            Assert.That(json, Does.Contain(Environment.NewLine));
            // Should contain camelCase keys
            Assert.That(json, Does.Contain("schemaVersion"));
            Assert.That(json, Does.Contain("deviceId"));
            Assert.That(json, Does.Contain("exportedAt"));
        }

        // ────────────────────────────────────────────────────────
        //  Merge engine tests
        // ────────────────────────────────────────────────────────

        [Test]
        public void Merge_LoopPoint_ZeroDoesNotOverrideSubstantial()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "base",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song.mp3", DurationMs = 10000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 500, LoopEnd = 9000, LastModified = 100 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "incoming",
                ExportedAt = 2000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "song.mp3", DurationMs = 10000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 0, LoopEnd = 0, LastModified = 200 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // Base substantive should survive
            Assert.That(result.Merged.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(500));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LoopEnd, Is.EqualTo(9000));
        }

        [Test]
        public void Merge_LoopPoint_SubstantialNewerWins()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 4000, LastModified = 100 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 2000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 200, LoopEnd = 4500, LastModified = 300 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            Assert.That(result.Merged.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(200));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LastModified, Is.EqualTo(300));
        }

        [Test]
        public void Merge_LoopPoint_OlderDoesNotOverrideNewer()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 2000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 500, LoopEnd = 4500, LastModified = 500 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 4000, LastModified = 100 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // Base (newer) should be kept
            Assert.That(result.Merged.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(500));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LastModified, Is.EqualTo(500));
        }

        [Test]
        public void Merge_Rating_ZeroDoesNotOverrideNonZero()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 1000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        Rating = new SyncRating { RatingValue = 4, LastModified = 100 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 2000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        Rating = new SyncRating { RatingValue = 0, LastModified = 200 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            Assert.That(result.Merged.Ratings, Has.Count.EqualTo(1));
            Assert.That(result.Merged.Ratings[0].Rating.RatingValue, Is.EqualTo(4));
        }

        [Test]
        public void Merge_Rating_NonZeroNewerWins()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 1000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        Rating = new SyncRating { RatingValue = 3, LastModified = 100 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 2000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000 },
                        Rating = new SyncRating { RatingValue = 5, LastModified = 300 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            Assert.That(result.Merged.Ratings, Has.Count.EqualTo(1));
            Assert.That(result.Merged.Ratings[0].Rating.RatingValue, Is.EqualTo(5));
        }

        [Test]
        public void Merge_Playlist_DifferentIdsSeparate()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 1000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist { Id = "pl-a", Name = "A", CreatedAt = 100, ModifiedAt = 100 }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 2000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist { Id = "pl-b", Name = "B", CreatedAt = 200, ModifiedAt = 200 }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            Assert.That(result.Merged.Playlists, Has.Count.EqualTo(2));
        }

        [Test]
        public void Merge_Playlist_SameIdMergesItems()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 1000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Merged", CreatedAt = 100, ModifiedAt = 100,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "a.mp3", DurationMs = 10000 },
                                SortOrder = 0
                            }
                        }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 2000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Merged", CreatedAt = 100, ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "a.mp3", DurationMs = 10000 },
                                SortOrder = 0
                            },
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "b.mp3", DurationMs = 20000 },
                                SortOrder = 1
                            }
                        }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // incoming has later ModifiedAt, so it wins
            Assert.That(result.Merged.Playlists, Has.Count.EqualTo(1));
            Assert.That(result.Merged.Playlists[0].Items, Has.Count.EqualTo(2));
            Assert.That(result.Merged.Playlists[0].Items[0].Song.FileName, Is.EqualTo("a.mp3"));
            Assert.That(result.Merged.Playlists[0].Items[1].Song.FileName, Is.EqualTo("b.mp3"));
            // sortOrders re-normalized
            Assert.That(result.Merged.Playlists[0].Items[0].SortOrder, Is.EqualTo(0));
            Assert.That(result.Merged.Playlists[0].Items[1].SortOrder, Is.EqualTo(1));
        }

        [Test]
        public void Merge_Playlist_ItemsDeduplicatedAcrossWinnerAndLoser()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 2000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Dedup", CreatedAt = 100, ModifiedAt = 300,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "a.mp3", DurationMs = 10000 },
                                SortOrder = 0
                            },
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "c.mp3", DurationMs = 30000 },
                                SortOrder = 1
                            }
                        }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 1000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Dedup", CreatedAt = 100, ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "a.mp3", DurationMs = 10000 },
                                SortOrder = 0
                            },
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "b.mp3", DurationMs = 20000 },
                                SortOrder = 1
                            }
                        }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // Base (ModifiedAt=300) wins. Items: a, c from winner + b (new from loser)
            Assert.That(result.Merged.Playlists, Has.Count.EqualTo(1));
            Assert.That(result.Merged.Playlists[0].Items, Has.Count.EqualTo(3));

            var names = result.Merged.Playlists[0].Items
                .Select(i => i.Song.FileName)
                .ToList();
            Assert.That(names, Does.Contain("a.mp3"));
            Assert.That(names, Does.Contain("b.mp3"));
            Assert.That(names, Does.Contain("c.mp3"));

            // sortOrders re-normalized
            for (int i = 0; i < result.Merged.Playlists[0].Items.Count; i++)
                Assert.That(result.Merged.Playlists[0].Items[i].SortOrder, Is.EqualTo(i));
        }

        // ────────────────────────────────────────────────────────
        //  Store export tests
        // ────────────────────────────────────────────────────────

        private string _storeDbPath;
        private DatabaseHelper _storeDbHelper;
        private SQLiteSyncSnapshotStore _store;

        [SetUp]
        public void StoreSetUp()
        {
            _storeDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"SyncStoreTest_{Guid.NewGuid()}.db");
            _storeDbHelper = new DatabaseHelper(_storeDbPath);
            _storeDbHelper.InitializeDatabase();
            _store = new SQLiteSyncSnapshotStore(_storeDbHelper);
        }

        [TearDown]
        public void StoreTearDown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(_storeDbPath))
            {
                try { File.Delete(_storeDbPath); } catch { }
            }
        }

        [Test]
        public void StoreExport_EmptyDb_ReturnsValidSnapshot()
        {
            var snap = _store.ExportSnapshotAsync().Result;

            Assert.That(snap.SchemaVersion, Is.EqualTo(2));
            Assert.That(snap.DeviceId, Is.Not.Null.And.Not.Empty);
            Assert.That(snap.ExportedAt, Is.GreaterThan(0));
            Assert.That(snap.Playlists, Is.Empty);
            Assert.That(snap.LoopPoints, Is.Empty);
            Assert.That(snap.Ratings, Is.Empty);
            Assert.That(snap.PlaybackStatistics, Is.Not.Null);
            Assert.That(snap.PlaybackStatistics.Devices, Is.Empty);
            Assert.That(snap.PlaybackStatistics.Songs, Is.Empty);
            Assert.That(snap.PlaybackStatistics.Tombstones, Is.Empty);
        }

        [Test]
        public void StoreExport_AlwaysEmitsCanonicalV2PlaybackStatistics()
        {
            using (var connection = _storeDbHelper.GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(connection);
                connection.Execute("INSERT INTO PlaybackSyncDevices VALUES ('device-a',0,'Desktop',1,'windows',1,1)");
            }

            var snap = _store.ExportSnapshotAsync().Result;
            Assert.That(snap.SchemaVersion, Is.EqualTo(2));
            Assert.That(snap.PlaybackStatistics, Is.Not.Null);
        }

        [Test]
        public void StoreExport_DeviceIdIsPersistent()
        {
            var snap1 = _store.ExportSnapshotAsync().Result;
            var snap2 = _store.ExportSnapshotAsync().Result;

            Assert.That(snap1.DeviceId, Is.EqualTo(snap2.DeviceId));
            Assert.That(snap1.DeviceId, Is.EqualTo(_storeDbHelper.GetSetting("Sync.DeviceId")));
        }

        [Test]
        public void StoreExport_ExportsSubstantialLoopPointsOnly()
        {
            SeedTrack(1, "song_a.mp3", 100000, 5000);
            SeedTrack(2, "song_b.mp3", 200000, 10000);
            SeedLoopPoint(1, 100, 90000, DateTime.Now.AddDays(-1));
            SeedLoopPoint(2, 0, 0, DateTime.Now); // non-substantial, should be excluded

            var snap = _store.ExportSnapshotAsync().Result;

            Assert.That(snap.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(snap.LoopPoints[0].Song.FileName, Is.EqualTo("song_a.mp3"));
            Assert.That(snap.LoopPoints[0].Song.DurationMs, Is.EqualTo(5000));
            Assert.That(snap.LoopPoints[0].Song.TotalSamples, Is.EqualTo(100000));
            Assert.That(snap.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(100));
            Assert.That(snap.LoopPoints[0].LoopPoint.LoopEnd, Is.EqualTo(90000));
        }

        [Test]
        public void StoreExport_ExportsNonZeroRatingsOnly()
        {
            SeedTrack(1, "song_a.mp3", 100000, 5000);
            SeedTrack(2, "song_b.mp3", 200000, 10000);
            SeedRating(1, 4, DateTime.Now.AddDays(-1));
            SeedRating(2, 0, DateTime.Now); // zero, should be excluded

            var snap = _store.ExportSnapshotAsync().Result;

            Assert.That(snap.Ratings, Has.Count.EqualTo(1));
            Assert.That(snap.Ratings[0].Song.FileName, Is.EqualTo("song_a.mp3"));
            Assert.That(snap.Ratings[0].Rating.RatingValue, Is.EqualTo(4));
        }

        [Test]
        public void StoreExport_ExportsPlaylistWithItems()
        {
            SeedTrack(1, "track1.mp3", 441000, 20000);
            SeedTrack(2, "track2.flac", 882000, 40000);
            int plId = SeedPlaylist("My Playlist");
            SeedPlaylistItem(plId, 1, 0);
            SeedPlaylistItem(plId, 2, 1);

            var snap = _store.ExportSnapshotAsync().Result;

            Assert.That(snap.Playlists, Has.Count.EqualTo(1));
            var pl = snap.Playlists[0];
            Assert.That(pl.Name, Is.EqualTo("My Playlist"));
            Assert.That(pl.Id, Is.Not.Null.And.Not.Empty);
            Assert.That(pl.CreatedAt, Is.GreaterThan(0));
            Assert.That(pl.ModifiedAt, Is.EqualTo(pl.CreatedAt)); // modifiedAt = createdAt
            Assert.That(pl.Items, Has.Count.EqualTo(2));
            Assert.That(pl.Items[0].SortOrder, Is.EqualTo(0));
            Assert.That(pl.Items[1].SortOrder, Is.EqualTo(1));
            Assert.That(pl.Items[0].Song.DurationMs, Is.EqualTo(20000));
            Assert.That(pl.Items[1].Song.DurationMs, Is.EqualTo(40000));
        }

        [Test]
        public void StoreExport_PlaylistMappingIsStable()
        {
            SeedTrack(1, "t.mp3", 1000, 10000);
            int plId = SeedPlaylist("Stable PL");
            SeedPlaylistItem(plId, 1, 0);

            var snap1 = _store.ExportSnapshotAsync().Result;
            var snap2 = _store.ExportSnapshotAsync().Result;

            Assert.That(snap1.Playlists[0].Id, Is.EqualTo(snap2.Playlists[0].Id),
                "Playlist sync id should be stable across exports");
        }

        [Test]
        public void StoreExport_IdentityTotalSamplesNullWhenNotPositive()
        {
            SeedTrack(1, "zero_samples.mp3", 0, 5000);
            SeedLoopPoint(1, 100, 4000, DateTime.Now);

            var snap = _store.ExportSnapshotAsync().Result;

            Assert.That(snap.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(snap.LoopPoints[0].Song.TotalSamples, Is.Null,
                "TotalSamples should be null when <= 0");
        }

        // ────────────────────────────────────────────────────────
        //  Store apply tests
        // ────────────────────────────────────────────────────────

        [Test]
        public void StoreApply_RejectsNonV2Snapshot()
        {
            var legacy = new SyncSnapshot
            {
                SchemaVersion = 1,
                DeviceId = "legacy",
                ExportedAt = 1
            };

            Assert.Throws<FormatException>(() => _store.ApplySnapshotAsync(legacy).GetAwaiter().GetResult());
        }

        [Test]
        public void StoreApply_UnknownSongSkipped()
        {
            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "nonexistent.mp3", DurationMs = 50000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 9000, LastModified = 100 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.SkippedUnmatched, Is.GreaterThan(0));
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(0));
        }

        [Test]
        public void StoreApply_AmbiguousNameSkipped()
        {
            // Two tracks with same name AND same durationMs -> Tier 1 should be ambiguous
            SeedTrack(1, "dupe.mp3", 100000, 5000);
            SeedTrack(2, "dupe.mp3", 200000, 5000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "dupe.mp3", DurationMs = 5000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 100, LoopEnd = 9000, LastModified = 100 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.SkippedAmbiguous, Is.GreaterThan(0),
                "Two tracks with same fileName+durationMs should be ambiguous");
        }

        [Test]
        public void StoreApply_LoopPointAppliedSuccessfully()
        {
            SeedTrack(1, "target.mp3", 441000, 20000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000, TotalSamples = 441000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 5000, LoopEnd = 400000, LastModified = 500 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(1));

            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                var lp = conn.QueryFirstOrDefault("SELECT * FROM LoopPoints WHERE TrackId = 1");
                Assert.That(lp, Is.Not.Null);
                Assert.That((long)lp.LoopStart, Is.EqualTo(5000));
                Assert.That((long)lp.LoopEnd, Is.EqualTo(400000));
            }
        }

        [Test]
        public void StoreApply_LoopPoint_LocalNewerKept()
        {
            SeedTrack(1, "target.mp3", 441000, 20000);
            SeedLoopPoint(1, 1000, 400000, DateTime.Now); // local: now

            // remote with older timestamp
            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000 },
                        LoopPoint = new SyncLoopPoint
                        {
                            LoopStart = 9999,
                            LoopEnd = 8888,
                            LastModified = new DateTimeOffset(DateTime.Now.AddDays(-10)).ToUnixTimeMilliseconds()
                        }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.SkippedLoopPoints, Is.EqualTo(1)); // local newer, skipped
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(0));
        }

        [Test]
        public void StoreApply_RatingAppliedSuccessfully()
        {
            SeedTrack(1, "target.mp3", 441000, 20000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000 },
                        Rating = new SyncRating { RatingValue = 5, LastModified = 500 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedRatings, Is.EqualTo(1));

            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                var r = conn.QueryFirstOrDefault("SELECT * FROM UserRatings WHERE TrackId = 1");
                Assert.That(r, Is.Not.Null);
                Assert.That((int)r.Rating, Is.EqualTo(5));
            }
        }

        [Test]
        public void StoreApply_Rating_LocalNewerKept()
        {
            SeedTrack(1, "target.mp3", 441000, 20000);
            SeedRating(1, 4, DateTime.Now); // local: now

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000 },
                        Rating = new SyncRating
                        {
                            RatingValue = 3,
                            LastModified = new DateTimeOffset(DateTime.Now.AddDays(-10)).ToUnixTimeMilliseconds()
                        }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.SkippedRatings, Is.EqualTo(1));
            Assert.That(result.AppliedRatings, Is.EqualTo(0));
        }

        [Test]
        public void StoreApply_PlaylistCreated()
        {
            SeedTrack(1, "song_a.mp3", 441000, 20000);
            SeedTrack(2, "song_b.mp3", 882000, 40000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "sync-pl-uuid",
                        Name = "Synced Playlist",
                        CreatedAt = 100,
                        ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "song_a.mp3", DurationMs = 20000 },
                                SortOrder = 0
                            },
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "song_b.mp3", DurationMs = 40000 },
                                SortOrder = 1
                            }
                        }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedPlaylists, Is.EqualTo(1));

            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                var pl = conn.QueryFirstOrDefault("SELECT * FROM Playlists WHERE Name = 'Synced Playlist'");
                Assert.That(pl, Is.Not.Null);

                int itemCount = conn.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Id", new { pl.Id });
                Assert.That(itemCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void StoreApply_Playlist_SkipsUnmatchedSongs()
        {
            SeedTrack(1, "existing.mp3", 441000, 20000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-uuid",
                        Name = "Partial",
                        CreatedAt = 100,
                        ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "existing.mp3", DurationMs = 20000 },
                                SortOrder = 0
                            },
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity { FileName = "missing.mp3", DurationMs = 30000 },
                                SortOrder = 1
                            }
                        }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;

            // 1 unmatched song
            Assert.That(result.SkippedUnmatched, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.AppliedPlaylists, Is.EqualTo(1));

            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                var pl = conn.QueryFirstOrDefault("SELECT * FROM Playlists WHERE Name = 'Partial'");
                Assert.That(pl, Is.Not.Null);

                int itemCount = conn.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Id", new { pl.Id });
                Assert.That(itemCount, Is.EqualTo(1), "Only the matched song should be in the playlist");
            }
        }

        [Test]
        public void StoreApply_LoopPointZeroZeroSkipped()
        {
            SeedTrack(1, "target.mp3", 441000, 20000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 0, LoopEnd = 0, LastModified = 500 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(0));
        }

        [Test]
        public void StoreExport_Tier1MatchingByDurationMs()
        {
            // Two tracks with same fileName but different durations
            SeedTrack(1, "ambiguous.mp3", 100000, 5000);
            SeedTrack(2, "ambiguous.mp3", 200000, 10000);

            // Remote has durationMs=10000, should match track 2 exactly (Tier 1)
            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "ambiguous.mp3", DurationMs = 10000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 500, LoopEnd = 190000, LastModified = 100 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            // Tier 1: fileName + exact durationMs -> only track 2 has durationMs=10000 -> unique -> success
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(1),
                "Tier 1 matching by fileName + exact durationMs should succeed");
        }

        // ────────────────────────────────────────────────────────
        //  Default-loop (0→TotalSamples) handling tests
        // ────────────────────────────────────────────────────────

        [Test]
        public void StoreExport_DoesNotExportDefaultFullTrackLoop()
        {
            // Desktop default: LoopStart=0, LoopEnd=TotalSamples — must NOT be exported
            long totalSamples = 882000;
            SeedTrack(1, "fulltrack.mp3", totalSamples, 20000);
            SeedLoopPoint(1, 0, totalSamples, DateTime.Now);

            var snap = _store.ExportSnapshotAsync().Result;

            Assert.That(snap.LoopPoints, Is.Empty,
                "Default full-track loop (0→TotalSamples) must not be exported");
        }

        [Test]
        public void StoreApply_RemoteZeroTotalSamples_DoesNotWriteLocal()
        {
            // Remote sends a loop point with LoopStart=0, LoopEnd=TotalSamples (desktop default)
            SeedTrack(1, "target.mp3", 882000, 20000);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000, TotalSamples = 882000 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 0, LoopEnd = 882000, LastModified = 500 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(0),
                "Remote default full-track loop (0→TotalSamples) should not be applied");

            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                var exists = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM LoopPoints WHERE TrackId = 1");
                Assert.That(exists, Is.EqualTo(0),
                    "No LoopPoints row should be created for a skipped default loop");
            }
        }

        [Test]
        public void StoreApply_LocalDefaultDoesNotBlockRemoteRealLoop()
        {
            // Local has desktop default (0→TotalSamples), remote sends a real loop → should overwrite
            long totalSamples = 882000;
            SeedTrack(1, "target.mp3", totalSamples, 20000);
            SeedLoopPoint(1, 0, totalSamples, DateTime.Now.AddDays(-1)); // local default, older

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "remote",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "target.mp3", DurationMs = 20000, TotalSamples = totalSamples },
                        LoopPoint = new SyncLoopPoint { LoopStart = 5000, LoopEnd = 800000, LastModified = 600 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(1),
                "Remote real loop point should overwrite local default (0→TotalSamples)");

            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                var lp = conn.QueryFirstOrDefault("SELECT * FROM LoopPoints WHERE TrackId = 1");
                Assert.That(lp, Is.Not.Null);
                Assert.That((long)lp.LoopStart, Is.EqualTo(5000));
                Assert.That((long)lp.LoopEnd, Is.EqualTo(800000));
            }
        }

        [Test]
        public void Merge_ZeroTotalSamples_DoesNotOverrideRealLoop()
        {
            // base has a real loop; incoming has 0→totalSamples → base should survive
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 2000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000, TotalSamples = 220500 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 500, LoopEnd = 200000, LastModified = 500 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "inc", ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000, TotalSamples = 220500 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 0, LoopEnd = 220500, LastModified = 600 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // Base real loop should survive (incoming 0→TotalSamples is unset)
            Assert.That(result.Merged.LoopPoints, Has.Count.EqualTo(1));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(500));
            Assert.That(result.Merged.LoopPoints[0].LoopPoint.LoopEnd, Is.EqualTo(200000));
        }

        [Test]
        public void Merge_BothSidesZeroTotalSamples_ResultIsUnset()
        {
            // Both sides have 0→totalSamples → merged result should not be substantial
            long totalSamples = 220500;
            var earlier = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "earlier", ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000, TotalSamples = totalSamples },
                        LoopPoint = new SyncLoopPoint { LoopStart = 0, LoopEnd = totalSamples, LastModified = 100 }
                    }
                }
            };

            var later = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "later", ExportedAt = 2000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "s.mp3", DurationMs = 5000, TotalSamples = totalSamples },
                        LoopPoint = new SyncLoopPoint { LoopStart = 0, LoopEnd = totalSamples, LastModified = 200 }
                    }
                }
            };

            // Merged result should be the later one (it has higher lastModified)
            var result = SyncMergeEngine.Merge(earlier, later);
            Assert.That(result.Merged.LoopPoints, Has.Count.EqualTo(1));

            // The merged loop point itself is 0→totalSamples, which should NOT be substantial
            var merged = result.Merged.LoopPoints[0].LoopPoint;
            Assert.That(SyncMergeEngine.IsSubstantialLoop(merged, totalSamples), Is.False,
                "Merged 0→TotalSamples should NOT be considered substantial");
            Assert.That(SyncMergeEngine.IsSubstantialLoop(merged), Is.True,
                "Without song context, 0→TotalSamples IS substantial (0/0 check only)");
        }

        // ────────────────────────────────────────────────────────────────
        //  totalSamples stability tests (micro-differences between devices)
        // ────────────────────────────────────────────────────────────────

        [Test]
        public void Merge_Rating_MicroDiffTotalSamples_KeepsRemoteIdentity()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "desktop", ExportedAt = 1000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity
                        {
                            FileName = "Summer Pockets.flac", DurationMs = 239987,
                            TotalSamples = 10583426 // desktop
                        },
                        Rating = new SyncRating { RatingValue = 4, LastModified = 100 }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "phone", ExportedAt = 2000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity
                        {
                            FileName = "Summer Pockets.flac", DurationMs = 239987,
                            TotalSamples = 10583412 // phone (differs by 14)
                        },
                        Rating = new SyncRating { RatingValue = 4, LastModified = 200 }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // Should be a single merged rating entry
            Assert.That(result.Merged.Ratings, Has.Count.EqualTo(1));
            // Remote totalSamples should be preserved (incoming wins)
            Assert.That(result.Merged.Ratings[0].Song.TotalSamples, Is.EqualTo(10583412),
                "Remote (phone) totalSamples should be preserved to avoid jitter");
            Assert.That(result.Merged.Ratings[0].Song.DurationMs, Is.EqualTo(239987));
        }

        [Test]
        public void Merge_PlaylistItem_MicroDiffTotalSamples_KeepsRemoteIdentity()
        {
            var baseSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "desktop", ExportedAt = 1000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Test", CreatedAt = 100, ModifiedAt = 100,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity
                                {
                                    FileName = "song_x.mp3", DurationMs = 200000,
                                    TotalSamples = 8820026 // desktop
                                },
                                SortOrder = 0
                            }
                        }
                    }
                }
            };

            var incomingSnap = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "phone", ExportedAt = 2000,
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "pl-1", Name = "Test", CreatedAt = 100, ModifiedAt = 200,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem
                            {
                                Song = new SyncSongIdentity
                                {
                                    FileName = "song_x.mp3", DurationMs = 200000,
                                    TotalSamples = 8820000 // phone (differs by 26)
                                },
                                SortOrder = 0
                            }
                        }
                    }
                }
            };

            var result = SyncMergeEngine.Merge(baseSnap, incomingSnap);

            // Incoming has later ModifiedAt, so incoming wins → items use incoming's song identity
            Assert.That(result.Merged.Playlists, Has.Count.EqualTo(1));
            Assert.That(result.Merged.Playlists[0].Items, Has.Count.EqualTo(1));
            // Song identity should be from incoming (phone) to avoid jitter
            Assert.That(result.Merged.Playlists[0].Items[0].Song.TotalSamples, Is.EqualTo(8820000));
        }

        [Test]
        public void Merge_V1Snapshots_AreRejected()
        {
            Assert.Throws<FormatException>(() => SyncMergeEngine.Merge(
                new SyncSnapshot { SchemaVersion = 1, DeviceId = "base", ExportedAt = 1 },
                new SyncSnapshot { SchemaVersion = 2, DeviceId = "incoming", ExportedAt = 2 }));
        }

        [Test]
        public void Merge_ResultIsDetachedAndPlaylistNormalizationDoesNotMutateInputs()
        {
            var baseSnapshot = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "base", ExportedAt = 1,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity { FileName = "loop-base.mp3", DurationMs = 10 },
                        LoopPoint = new SyncLoopPoint { LoopStart = 1, LoopEnd = 9, LastModified = 1 }
                    }
                },
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "shared", Name = "Base", CreatedAt = 1, ModifiedAt = 2,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem { Song = new SyncSongIdentity { FileName = "base-item.mp3", DurationMs = 10 }, SortOrder = 7 }
                        }
                    }
                }
            };
            var incomingSnapshot = new SyncSnapshot
            {
                SchemaVersion = 2, DeviceId = "incoming", ExportedAt = 2,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity { FileName = "rating-incoming.mp3", DurationMs = 20 },
                        Rating = new SyncRating { RatingValue = 4, LastModified = 2 }
                    }
                },
                Playlists = new List<SyncPlaylist>
                {
                    new SyncPlaylist
                    {
                        Id = "shared", Name = "Incoming", CreatedAt = 1, ModifiedAt = 1,
                        Items = new List<SyncPlaylistItem>
                        {
                            new SyncPlaylistItem { Song = new SyncSongIdentity { FileName = "incoming-item.mp3", DurationMs = 20 }, SortOrder = 9 }
                        }
                    }
                }
            };

            var merged = SyncMergeEngine.Merge(baseSnapshot, incomingSnapshot).Merged;
            var mergedLoop = merged.LoopPoints.Single();
            var mergedRating = merged.Ratings.Single();
            var mergedPlaylist = merged.Playlists.Single();

            Assert.That(mergedLoop, Is.Not.SameAs(baseSnapshot.LoopPoints[0]));
            Assert.That(mergedLoop.Song, Is.Not.SameAs(baseSnapshot.LoopPoints[0].Song));
            Assert.That(mergedRating, Is.Not.SameAs(incomingSnapshot.Ratings[0]));
            Assert.That(mergedRating.Rating, Is.Not.SameAs(incomingSnapshot.Ratings[0].Rating));
            Assert.That(mergedPlaylist, Is.Not.SameAs(baseSnapshot.Playlists[0]));
            Assert.That(mergedPlaylist.Items[0], Is.Not.SameAs(baseSnapshot.Playlists[0].Items[0]));
            Assert.That(mergedPlaylist.Items[0].Song, Is.Not.SameAs(baseSnapshot.Playlists[0].Items[0].Song));
            Assert.That(mergedPlaylist.Items.Select(x => x.SortOrder), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(baseSnapshot.Playlists[0].Items[0].SortOrder, Is.EqualTo(7));
            Assert.That(incomingSnapshot.Playlists[0].Items[0].SortOrder, Is.EqualTo(9));

            mergedLoop.Song.FileName = "mutated-loop.mp3";
            mergedLoop.LoopPoint.LoopStart = 99;
            mergedRating.Song.FileName = "mutated-rating.mp3";
            mergedRating.Rating.RatingValue = 1;
            mergedPlaylist.Name = "mutated-playlist";
            mergedPlaylist.Items[0].Song.FileName = "mutated-item.mp3";
            mergedPlaylist.Items[0].SortOrder = 42;

            Assert.That(baseSnapshot.LoopPoints[0].Song.FileName, Is.EqualTo("loop-base.mp3"));
            Assert.That(baseSnapshot.LoopPoints[0].LoopPoint.LoopStart, Is.EqualTo(1));
            Assert.That(incomingSnapshot.Ratings[0].Song.FileName, Is.EqualTo("rating-incoming.mp3"));
            Assert.That(incomingSnapshot.Ratings[0].Rating.RatingValue, Is.EqualTo(4));
            Assert.That(baseSnapshot.Playlists[0].Name, Is.EqualTo("Base"));
            Assert.That(baseSnapshot.Playlists[0].Items[0].Song.FileName, Is.EqualTo("base-item.mp3"));
            Assert.That(baseSnapshot.Playlists[0].Items[0].SortOrder, Is.EqualTo(7));
            Assert.That(incomingSnapshot.Playlists[0].Items[0].Song.FileName, Is.EqualTo("incoming-item.mp3"));
            Assert.That(incomingSnapshot.Playlists[0].Items[0].SortOrder, Is.EqualTo(9));
        }

        [Test]
        public void StoreApply_MicroDiffTotalSamples_SameDuration_MatchesByTier1()
        {
            // Local track: TotalSamples=10583426 (desktop)
            // Remote: TotalSamples=10583412 (phone), DurationMs same=239987
            // Tier 1 (fileName + exact durationMs) should match without needing totalSamples
            SeedTrack(1, "Summer Pockets.flac", 10583426, 239987);

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "phone",
                ExportedAt = 1000,
                Ratings = new List<SyncRatingEntry>
                {
                    new SyncRatingEntry
                    {
                        Song = new SyncSongIdentity
                        {
                            FileName = "Summer Pockets.flac",
                            DurationMs = 239987,
                            TotalSamples = 10583412 // differs from local by 14
                        },
                        Rating = new SyncRating { RatingValue = 5, LastModified = 500 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedRatings, Is.EqualTo(1),
                "Same fileName+DURATION should match despite totalSamples micro-diff");
        }

        [Test]
        public void StoreApply_TotalSamplesTolerance_MatchesMicroDiff()
        {
            // Local tracks with same fileName and same totalSamples (but remote differs slightly)
            // This tests the ±10000 tolerance for cases where durationMs=0
            SeedTrack(1, "summer.flac", 10583426, 0); // durationMs unknown

            var snap = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = "phone",
                ExportedAt = 1000,
                LoopPoints = new List<SyncLoopPointEntry>
                {
                    new SyncLoopPointEntry
                    {
                        Song = new SyncSongIdentity
                        {
                            FileName = "summer.flac",
                            DurationMs = 0,
                            TotalSamples = 10583412 // differs by 14, within ±10000
                        },
                        LoopPoint = new SyncLoopPoint { LoopStart = 1000, LoopEnd = 900000, LastModified = 500 }
                    }
                }
            };

            var result = _store.ApplySnapshotAsync(snap).Result;
            Assert.That(result.AppliedLoopPoints, Is.EqualTo(1),
                "Tier 2 should match with ±10000 tolerance on totalSamples");
        }

        [Test]
        public void StoreApply_Schema2PlaybackStatistics_AppliesAndExportRemainsV2()
        {
            InsertPlaybackTrack(1, "Café.mp3", 123456);

            var snapshot = SyncSnapshotSerializer.Deserialize(@"{
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
            ""datedListenMs"": { ""2026-07-10"": 60000 },
            ""undatedListenMs"": 12000,
            ""firstPlayedAtUtcMs"": 1783717800000,
            ""lastPlayedAtUtcMs"": 1783779600000,
            ""updatedAtUtcMs"": 1783779600000
          }
        ]
      }
    ],
    ""tombstones"": []
  }
}");

            var result = _store.ApplySnapshotAsync(snapshot).Result;
            Assert.That(result.AppliedLoopPoints + result.AppliedRatings + result.AppliedPlaylists, Is.EqualTo(0));

            using (var connection = _storeDbHelper.GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(connection);
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaybackSyncContributions"), Is.EqualTo(1));
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaybackSyncSongs WHERE LocalTrackId IS NOT NULL"), Is.EqualTo(1));
            }

            var exported = _store.ExportSnapshotAsync().Result;
            Assert.That(exported.SchemaVersion, Is.EqualTo(2));
            Assert.That(exported.PlaybackStatistics, Is.Not.Null);
        }

        [Test]
        public void StoreApply_Schema2PlatformConflict_RollsBackStatisticsTransaction()
        {
            using (var connection = _storeDbHelper.GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(connection);
                connection.Execute("INSERT INTO PlaybackSyncDevices VALUES ('device-a',0,'Desktop',1,'windows',1,1)");
            }

            var snapshot = SyncSnapshotSerializer.Deserialize(@"{
  ""schemaVersion"": 2,
  ""deviceId"": ""device-a"",
  ""exportedAt"": 1,
  ""playlists"": [],
  ""loopPoints"": [],
  ""ratings"": [],
  ""playbackStatistics"": {
    ""dateBucketBasis"": ""sourceLocal"",
    ""devices"": [
      {
        ""deviceId"": ""device-a"",
        ""displayName"": ""Phone"",
        ""firstSeenAtUtcMs"": 0,
        ""lastSeenAtUtcMs"": 1,
        ""currentGeneration"": 1,
        ""platform"": ""android"",
        ""displayNameUpdatedAtUtcMs"": 1
      }
    ],
    ""songs"": [
      {
        ""song"": {
          ""fileName"": ""rollback.mp3"",
          ""durationMs"": 1000,
          ""normalizedFileName"": ""rollback.mp3""
        },
        ""contributions"": []
      }
    ],
    ""tombstones"": []
  }
}");

            Assert.Throws<AggregateException>(() =>
            {
                var _ = _store.ApplySnapshotAsync(snapshot).Result;
            });

            using (var connection = _storeDbHelper.GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(connection);
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaybackSyncSongs"), Is.EqualTo(0));
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(1) FROM PlaybackSyncDevices WHERE DeviceId='device-a' AND Platform='windows'"), Is.EqualTo(1));
            }
        }

        // ────────────────────────────────────────────────────────
        //  Seed helpers
        // ────────────────────────────────────────────────────────

        private void SeedTrack(int id, string fileName, long totalSamples, long durationMs)
        {
            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
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
            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
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
            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
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
            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(
                    "INSERT INTO Playlists (Name) VALUES (@Name); SELECT last_insert_rowid();",
                    new { Name = name });
            }
        }

        private void SeedPlaylistItem(int playlistId, int songId, int sortOrder)
        {
            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute(
                    "INSERT OR IGNORE INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@Pid, @Sid, @Order)",
                    new { Pid = playlistId, Sid = songId, Order = sortOrder });
            }
        }

        private void InsertPlaybackTrack(int id, string fileName, long durationMs)
        {
            using (var conn = new SQLiteConnection($"Data Source={_storeDbPath};Version=3;"))
            {
                conn.Open();
                conn.Execute("INSERT OR REPLACE INTO Tracks (Id, FileName, FilePath, TotalSamples, DurationMs, LastModified) VALUES (@Id, @FileName, @FilePath, 1, @DurationMs, @Now)",
                    new { Id = id, FileName = fileName, FilePath = @"C:\Music\" + fileName, DurationMs = durationMs, Now = DateTime.Now });
            }
        }
    }
}
