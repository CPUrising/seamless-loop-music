using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    public sealed class PlaybackStatisticsSyncSnapshotAdapter : IPlaybackStatisticsSyncSnapshotAdapter
    {
        private readonly IDatabaseHelper _db;

        public PlaybackStatisticsSyncSnapshotAdapter(IDatabaseHelper db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public SyncPlaybackStatistics Export(IDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            PlaybackStatisticsSyncSchema.EnsureSchema(connection);
            RepairCurrentLocalDevice(connection);

            var devices = connection.Query<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices ORDER BY DeviceId").ToList();
            var songs = connection.Query<PlaybackSyncSong>("SELECT * FROM PlaybackSyncSongs ORDER BY SongId").ToList();
            var contributions = connection.Query<PlaybackSyncContribution>("SELECT * FROM PlaybackSyncContributions ORDER BY SongId,DeviceId,Generation").ToList();
            var buckets = connection.Query<PlaybackSyncDailyBucket>("SELECT * FROM PlaybackSyncDailyBuckets ORDER BY SongId,DeviceId,Generation,LocalDate").ToList();
            var tombstones = connection.Query<PlaybackSyncTombstone>("SELECT * FROM PlaybackSyncTombstones ORDER BY DeviceId,Generation,Scope").ToList();

            var tombstonedKeys = new HashSet<string>(tombstones.Select(TombstoneKey), StringComparer.Ordinal);
            var bucketLookup = buckets.ToLookup(x => ContributionKey(x.SongId, x.DeviceId, x.Generation), StringComparer.Ordinal);

            var statistics = new SyncPlaybackStatistics
            {
                DateBucketBasis = SyncSnapshotSerializer.SourceLocalDateBucketBasis,
                Devices = devices.Select(device => new SyncPlaybackDevice
                {
                    DeviceId = device.DeviceId,
                    CurrentGeneration = device.CurrentGeneration,
                    DisplayName = device.DisplayName,
                    DisplayNameUpdatedAtUtcMs = device.DisplayNameUpdatedAtUtcMs,
                    Platform = device.Platform,
                    FirstSeenAtUtcMs = device.FirstSeenAtUtcMs,
                    LastSeenAtUtcMs = device.LastSeenAtUtcMs
                }).ToList(),
                Songs = songs.Select(song => new SyncPlaybackSong
                {
                    Song = new SyncPlaybackSongIdentity
                    {
                        FileName = song.FileName,
                        NormalizedFileName = song.NormalizedFileName,
                        DurationMs = song.DurationMs,
                        TotalSamples = song.TotalSamples,
                        ContentHash = song.ContentHash
                    },
                    Contributions = contributions
                        .Where(contribution => contribution.SongId == song.SongId && !tombstonedKeys.Contains(TombstoneKey(contribution.DeviceId, contribution.Generation)))
                        .Select(contribution => new SyncPlaybackContribution
                        {
                            DeviceId = contribution.DeviceId,
                            Generation = contribution.Generation,
                            DatedListenMs = bucketLookup[ContributionKey(song.SongId, contribution.DeviceId, contribution.Generation)]
                                .ToDictionary(x => x.LocalDate, x => x.ListenMs, StringComparer.Ordinal),
                            UndatedListenMs = contribution.UndatedListenMs,
                            FirstPlayedAtUtcMs = contribution.FirstPlayedAtUtcMs,
                            LastPlayedAtUtcMs = contribution.LastPlayedAtUtcMs,
                            UpdatedAtUtcMs = contribution.UpdatedAtUtcMs
                        })
                        .ToList()
                }).ToList(),
                Tombstones = tombstones.Select(tombstone => new SyncPlaybackTombstone
                {
                    DeviceId = tombstone.DeviceId,
                    Generation = tombstone.Generation,
                    Scope = tombstone.Scope,
                    TombstonedAtUtcMs = tombstone.TombstonedAtUtcMs,
                    TombstonedByDeviceId = tombstone.TombstonedByDeviceId,
                    Reason = tombstone.Reason
                }).ToList()
            };

            return PlaybackStatisticsSyncCanonicalizer.Canonicalize(statistics);
        }

        public void Apply(IDbConnection connection, IDbTransaction transaction, SyncPlaybackStatistics statistics)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            PlaybackStatisticsSyncSchema.EnsureSchema(connection);
            var incoming = NormalizeIncomingStatistics(statistics);

            ValidatePlatformConsistencyAgainstExistingDevices(connection, transaction, incoming.Devices);

            foreach (var device in incoming.Devices)
                UpsertDevice(connection, transaction, device);

            var songIdsByKey = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var song in incoming.Songs)
            {
                var songId = UpsertSong(connection, transaction, song.Song);
                songIdsByKey.Add(SongKey(song.Song.NormalizedFileName, song.Song.DurationMs), songId);
            }

            foreach (var tombstone in incoming.Tombstones)
                UpsertTombstone(connection, transaction, tombstone);

            var activeTombstones = connection.Query<PlaybackSyncTombstone>(
                "SELECT * FROM PlaybackSyncTombstones ORDER BY DeviceId,Generation,Scope",
                transaction: transaction)
                .ToList();

            foreach (var tombstone in activeTombstones)
            {
                connection.Execute(
                    "DELETE FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId AND Generation=@Generation",
                    new { tombstone.DeviceId, tombstone.Generation },
                    transaction);
            }

            var tombstonedKeys = new HashSet<string>(activeTombstones.Select(TombstoneKey), StringComparer.Ordinal);
            foreach (var song in incoming.Songs)
            {
                var songId = songIdsByKey[SongKey(song.Song.NormalizedFileName, song.Song.DurationMs)];
                foreach (var contribution in song.Contributions)
                {
                    if (tombstonedKeys.Contains(TombstoneKey(contribution.DeviceId, contribution.Generation)))
                        continue;

                    UpsertContribution(connection, transaction, songId, contribution);
                }
            }
        }

        public int RelinkExactAndUniqueFuzzy()
        {
            using (var connection = _db.GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(connection);
                connection.Execute("BEGIN IMMEDIATE;");
                try
                {
                    var linked = PlaybackStatisticsSyncPersistence.RelinkSongs(connection, null);
                    connection.Execute("COMMIT;");
                    return linked;
                }
                catch
                {
                    try { connection.Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        private static SyncPlaybackStatistics NormalizeIncomingStatistics(SyncPlaybackStatistics statistics)
        {
            var canonical = PlaybackStatisticsSyncCanonicalizer.Merge(PlaybackStatisticsSyncCanonicalizer.Empty(), statistics ?? PlaybackStatisticsSyncCanonicalizer.Empty());
            ValidateStatistics(canonical);
            return canonical;
        }

        private static void ValidateStatistics(SyncPlaybackStatistics statistics)
        {
            if (statistics.DateBucketBasis != SyncSnapshotSerializer.SourceLocalDateBucketBasis)
                throw new FormatException($"Unsupported dateBucketBasis: {statistics.DateBucketBasis}.");

            var deviceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in statistics.Devices ?? new List<SyncPlaybackDevice>())
            {
                if (device == null || string.IsNullOrWhiteSpace(device.DeviceId)) throw new FormatException("Playback deviceId is required.");
                if (string.IsNullOrWhiteSpace(device.DisplayName)) throw new FormatException("Playback device displayName is required and must not be blank.");
                if (device.CurrentGeneration < 0 || device.FirstSeenAtUtcMs < 0 || device.LastSeenAtUtcMs < 0) throw new FormatException("Playback device generation and timestamps must be >= 0.");
                if (device.DisplayNameUpdatedAtUtcMs <= 0) throw new FormatException("Playback device displayNameUpdatedAtUtcMs must be positive.");
                if (device.Platform != "android" && device.Platform != "windows") throw new FormatException("Playback device platform must be android or windows.");
                if (device.FirstSeenAtUtcMs > device.LastSeenAtUtcMs) throw new FormatException("Playback device firstSeenAtUtcMs must not be after lastSeenAtUtcMs.");
                deviceIds.Add(device.DeviceId);
            }

            foreach (var playbackSong in statistics.Songs ?? new List<SyncPlaybackSong>())
            {
                if (playbackSong?.Song == null || playbackSong.Contributions == null) throw new FormatException("Playback song and contributions are required.");
                var song = playbackSong.Song;
                if (string.IsNullOrWhiteSpace(song.FileName) || string.IsNullOrWhiteSpace(song.NormalizedFileName)) throw new FormatException("Playback song identity is required.");
                if (song.DurationMs < 0 || (song.TotalSamples.HasValue && song.TotalSamples.Value < 0)) throw new FormatException("Playback song counters must be >= 0.");
                if (!string.Equals(song.NormalizedFileName, SyncSnapshotSerializer.NormalizePlaybackSongFileName(song.FileName), StringComparison.Ordinal)) throw new FormatException("Playback normalizedFileName does not match fileName.");

                foreach (var contribution in playbackSong.Contributions)
                {
                    if (contribution == null || string.IsNullOrWhiteSpace(contribution.DeviceId) || contribution.DatedListenMs == null) throw new FormatException("Playback contribution fields are required.");
                    if (!deviceIds.Contains(contribution.DeviceId)) throw new FormatException("Playback contribution references an unregistered device.");
                    if (contribution.Generation < 0 || contribution.UndatedListenMs < 0 || contribution.UpdatedAtUtcMs < 0 || (contribution.FirstPlayedAtUtcMs.HasValue && contribution.FirstPlayedAtUtcMs.Value < 0) || (contribution.LastPlayedAtUtcMs.HasValue && contribution.LastPlayedAtUtcMs.Value < 0)) throw new FormatException("Playback contribution counters and timestamps must be >= 0.");
                    if (contribution.FirstPlayedAtUtcMs.GetValueOrDefault() != 0 && contribution.LastPlayedAtUtcMs.GetValueOrDefault() != 0 && contribution.FirstPlayedAtUtcMs > contribution.LastPlayedAtUtcMs) throw new FormatException("Playback contribution firstPlayedAtUtcMs must not be after lastPlayedAtUtcMs.");
                    foreach (var dated in contribution.DatedListenMs)
                    {
                        if (!IsExactDate(dated.Key) || dated.Value < 0) throw new FormatException("Playback datedListenMs requires valid yyyy-MM-dd dates and non-negative counters.");
                    }
                }
            }

            foreach (var tombstone in statistics.Tombstones ?? new List<SyncPlaybackTombstone>())
            {
                if (tombstone == null || string.IsNullOrWhiteSpace(tombstone.DeviceId) || string.IsNullOrWhiteSpace(tombstone.TombstonedByDeviceId) || string.IsNullOrWhiteSpace(tombstone.Reason)) throw new FormatException("Playback tombstone deviceId, tombstonedByDeviceId, and reason are required.");
                if (!deviceIds.Contains(tombstone.DeviceId)) throw new FormatException("Playback tombstone references an unregistered device.");
                if (!deviceIds.Contains(tombstone.TombstonedByDeviceId)) throw new FormatException("Playback tombstone actor references an unregistered device.");
                if (tombstone.Generation < 0 || tombstone.TombstonedAtUtcMs < 0) throw new FormatException("Playback tombstone counters and timestamps must be >= 0.");
                if (tombstone.Scope != SyncSnapshotSerializer.DeviceGenerationTombstoneScope) throw new FormatException($"Unsupported playback tombstone scope: {tombstone.Scope}.");
            }
        }

        private void RepairCurrentLocalDevice(IDbConnection connection)
        {
            var deviceId = _db.GetSetting(PlaybackStatisticsDeviceIdentity.DeviceKey);
            if (string.IsNullOrWhiteSpace(deviceId)) return;

            var device = connection.QueryFirstOrDefault<PlaybackSyncDevice>(
                "SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId",
                new { DeviceId = deviceId });
            if (device == null || (!string.IsNullOrWhiteSpace(device.DisplayName) && device.DisplayNameUpdatedAtUtcMs > 0)) return;

            if (string.IsNullOrWhiteSpace(device.DisplayName))
                device.DisplayName = PlaybackStatisticsDeviceIdentity.CurrentWindowsDisplayName();
            device.DisplayNameUpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            connection.Execute(
                "UPDATE PlaybackSyncDevices SET DisplayName=@DisplayName, DisplayNameUpdatedAtUtcMs=@DisplayNameUpdatedAtUtcMs WHERE DeviceId=@DeviceId",
                device);
        }

        private static void ValidatePlatformConsistencyAgainstExistingDevices(IDbConnection connection, IDbTransaction transaction, IEnumerable<SyncPlaybackDevice> devices)
        {
            var incomingById = devices.ToDictionary(x => x.DeviceId, x => x.Platform, StringComparer.Ordinal);
            if (incomingById.Count == 0)
                return;

            var existing = connection.Query<PlaybackSyncDevice>(
                "SELECT * FROM PlaybackSyncDevices WHERE DeviceId IN @DeviceIds",
                new { DeviceIds = incomingById.Keys.ToArray() },
                transaction).ToList();

            foreach (var device in existing)
            {
                if (!string.Equals(device.Platform, incomingById[device.DeviceId], StringComparison.Ordinal))
                    throw new FormatException($"Conflicting platforms for playback device '{device.DeviceId}'.");
            }
        }

        private static void UpsertDevice(IDbConnection connection, IDbTransaction transaction, SyncPlaybackDevice incoming)
        {
            var existing = connection.QueryFirstOrDefault<PlaybackSyncDevice>(
                "SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId",
                new { incoming.DeviceId },
                transaction);

            if (existing == null)
            {
                connection.Execute(@"INSERT INTO PlaybackSyncDevices VALUES (@DeviceId,@CurrentGeneration,@DisplayName,@DisplayNameUpdatedAtUtcMs,@Platform,@FirstSeenAtUtcMs,@LastSeenAtUtcMs)", new
                {
                    incoming.DeviceId,
                    incoming.CurrentGeneration,
                    incoming.DisplayName,
                    incoming.DisplayNameUpdatedAtUtcMs,
                    incoming.Platform,
                    incoming.FirstSeenAtUtcMs,
                    incoming.LastSeenAtUtcMs
                }, transaction);
                return;
            }

            var displayName = existing.DisplayName;
            var displayNameUpdatedAtUtcMs = existing.DisplayNameUpdatedAtUtcMs;
            if (CompareDisplay(incoming.DisplayNameUpdatedAtUtcMs, incoming.DisplayName, existing.DisplayNameUpdatedAtUtcMs, existing.DisplayName) > 0)
            {
                displayName = incoming.DisplayName;
                displayNameUpdatedAtUtcMs = incoming.DisplayNameUpdatedAtUtcMs;
            }

            connection.Execute(@"UPDATE PlaybackSyncDevices SET CurrentGeneration=@CurrentGeneration, DisplayName=@DisplayName, DisplayNameUpdatedAtUtcMs=@DisplayNameUpdatedAtUtcMs, FirstSeenAtUtcMs=@FirstSeenAtUtcMs, LastSeenAtUtcMs=@LastSeenAtUtcMs WHERE DeviceId=@DeviceId",
                new
                {
                    DeviceId = incoming.DeviceId,
                    CurrentGeneration = Math.Max(existing.CurrentGeneration, incoming.CurrentGeneration),
                    DisplayName = displayName,
                    DisplayNameUpdatedAtUtcMs = displayNameUpdatedAtUtcMs,
                    FirstSeenAtUtcMs = Math.Min(existing.FirstSeenAtUtcMs, incoming.FirstSeenAtUtcMs),
                    LastSeenAtUtcMs = Math.Max(existing.LastSeenAtUtcMs, incoming.LastSeenAtUtcMs)
                },
                transaction);
        }

        private static long UpsertSong(IDbConnection connection, IDbTransaction transaction, SyncPlaybackSongIdentity incoming)
        {
            var existing = connection.QueryFirstOrDefault<PlaybackSyncSong>(
                "SELECT * FROM PlaybackSyncSongs WHERE NormalizedFileName=@NormalizedFileName AND DurationMs=@DurationMs",
                new { incoming.NormalizedFileName, incoming.DurationMs },
                transaction);

            if (existing == null)
            {
                connection.Execute(@"INSERT INTO PlaybackSyncSongs (NormalizedFileName,FileName,DurationMs,TotalSamples,ContentHash,LocalTrackId) VALUES (@NormalizedFileName,@FileName,@DurationMs,@TotalSamples,@ContentHash,NULL)", new
                {
                    incoming.NormalizedFileName,
                    incoming.FileName,
                    incoming.DurationMs,
                    incoming.TotalSamples,
                    incoming.ContentHash
                }, transaction);
                return connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
            }

            connection.Execute(@"UPDATE PlaybackSyncSongs SET FileName=@FileName, TotalSamples=@TotalSamples, ContentHash=@ContentHash WHERE SongId=@SongId",
                new
                {
                    SongId = existing.SongId,
                    FileName = OrdinalMax(existing.FileName, incoming.FileName),
                    TotalSamples = MaxNullable(existing.TotalSamples, incoming.TotalSamples),
                    ContentHash = OrdinalMax(existing.ContentHash, incoming.ContentHash)
                },
                transaction);

            return existing.SongId;
        }

        private static void UpsertTombstone(IDbConnection connection, IDbTransaction transaction, SyncPlaybackTombstone incoming)
        {
            var existing = connection.QueryFirstOrDefault<PlaybackSyncTombstone>(
                "SELECT * FROM PlaybackSyncTombstones WHERE DeviceId=@DeviceId AND Generation=@Generation AND Scope=@Scope",
                new { incoming.DeviceId, incoming.Generation, incoming.Scope },
                transaction);

            if (existing == null)
            {
                connection.Execute(@"INSERT INTO PlaybackSyncTombstones VALUES (@DeviceId,@Generation,@Scope,@TombstonedAtUtcMs,@TombstonedByDeviceId,@Reason)", new
                {
                    incoming.DeviceId,
                    incoming.Generation,
                    incoming.Scope,
                    incoming.TombstonedAtUtcMs,
                    incoming.TombstonedByDeviceId,
                    incoming.Reason
                }, transaction);
                return;
            }

            if (CompareTombstone(incoming, existing) <= 0)
                return;

            connection.Execute(@"UPDATE PlaybackSyncTombstones SET TombstonedAtUtcMs=@TombstonedAtUtcMs, TombstonedByDeviceId=@TombstonedByDeviceId, Reason=@Reason WHERE DeviceId=@DeviceId AND Generation=@Generation AND Scope=@Scope",
                new
                {
                    incoming.DeviceId,
                    incoming.Generation,
                    incoming.Scope,
                    incoming.TombstonedAtUtcMs,
                    incoming.TombstonedByDeviceId,
                    incoming.Reason
                },
                transaction);
        }

        private static void UpsertContribution(IDbConnection connection, IDbTransaction transaction, long songId, SyncPlaybackContribution incoming)
        {
            var existing = connection.QueryFirstOrDefault<PlaybackSyncContribution>(
                "SELECT * FROM PlaybackSyncContributions WHERE SongId=@SongId AND DeviceId=@DeviceId AND Generation=@Generation",
                new { SongId = songId, incoming.DeviceId, incoming.Generation },
                transaction);

            if (existing == null)
            {
                connection.Execute(@"INSERT INTO PlaybackSyncContributions VALUES (@SongId,@DeviceId,@Generation,@UndatedListenMs,@FirstPlayedAtUtcMs,@LastPlayedAtUtcMs,@UpdatedAtUtcMs)", new
                {
                    SongId = songId,
                    incoming.DeviceId,
                    incoming.Generation,
                    incoming.UndatedListenMs,
                    incoming.FirstPlayedAtUtcMs,
                    incoming.LastPlayedAtUtcMs,
                    incoming.UpdatedAtUtcMs
                }, transaction);
            }
            else
            {
                connection.Execute(@"UPDATE PlaybackSyncContributions SET UndatedListenMs=@UndatedListenMs, FirstPlayedAtUtcMs=@FirstPlayedAtUtcMs, LastPlayedAtUtcMs=@LastPlayedAtUtcMs, UpdatedAtUtcMs=@UpdatedAtUtcMs WHERE SongId=@SongId AND DeviceId=@DeviceId AND Generation=@Generation",
                    new
                    {
                        SongId = songId,
                        incoming.DeviceId,
                        incoming.Generation,
                        UndatedListenMs = Math.Max(existing.UndatedListenMs, incoming.UndatedListenMs),
                        FirstPlayedAtUtcMs = MinNonZero(existing.FirstPlayedAtUtcMs, incoming.FirstPlayedAtUtcMs),
                        LastPlayedAtUtcMs = MaxNullable(existing.LastPlayedAtUtcMs, incoming.LastPlayedAtUtcMs),
                        UpdatedAtUtcMs = Math.Max(existing.UpdatedAtUtcMs, incoming.UpdatedAtUtcMs)
                    },
                    transaction);
            }

            foreach (var dated in incoming.DatedListenMs.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                connection.Execute(@"INSERT INTO PlaybackSyncDailyBuckets VALUES (@SongId,@DeviceId,@Generation,@LocalDate,@ListenMs) ON CONFLICT(SongId,DeviceId,Generation,LocalDate) DO UPDATE SET ListenMs=CASE WHEN ListenMs>excluded.ListenMs THEN ListenMs ELSE excluded.ListenMs END",
                    new
                    {
                        SongId = songId,
                        incoming.DeviceId,
                        incoming.Generation,
                        LocalDate = dated.Key,
                        ListenMs = dated.Value
                    },
                    transaction);
            }
        }

        private static int CompareDisplay(long leftUpdatedAtUtcMs, string leftDisplayName, long rightUpdatedAtUtcMs, string rightDisplayName)
        {
            var timestamp = leftUpdatedAtUtcMs.CompareTo(rightUpdatedAtUtcMs);
            return timestamp != 0 ? timestamp : string.Compare(leftDisplayName, rightDisplayName, StringComparison.Ordinal);
        }

        private static int CompareTombstone(SyncPlaybackTombstone incoming, PlaybackSyncTombstone existing)
        {
            var timestamp = incoming.TombstonedAtUtcMs.CompareTo(existing.TombstonedAtUtcMs);
            if (timestamp != 0) return timestamp;
            var actor = string.Compare(incoming.TombstonedByDeviceId, existing.TombstonedByDeviceId, StringComparison.Ordinal);
            return actor != 0 ? actor : string.Compare(incoming.Reason, existing.Reason, StringComparison.Ordinal);
        }

        private static string SongKey(string normalizedFileName, long durationMs) => normalizedFileName + "\u001f" + durationMs.ToString(CultureInfo.InvariantCulture);
        private static string TombstoneKey(PlaybackSyncTombstone tombstone) => TombstoneKey(tombstone.DeviceId, tombstone.Generation);
        private static string TombstoneKey(string deviceId, long generation) => deviceId + "\u001f" + generation.ToString(CultureInfo.InvariantCulture);
        private static string ContributionKey(long songId, string deviceId, long generation) => songId.ToString(CultureInfo.InvariantCulture) + "\u001f" + deviceId + "\u001f" + generation.ToString(CultureInfo.InvariantCulture);
        private static string OrdinalMax(string left, string right) => new[] { left, right }.Where(x => x != null).OrderBy(x => x, StringComparer.Ordinal).LastOrDefault();
        private static long? MaxNullable(long? left, long? right) => !left.HasValue ? right : !right.HasValue ? left : Math.Max(left.Value, right.Value);
        private static long? MinNonZero(long? left, long? right) { if (!left.HasValue || left.Value == 0) return right; if (!right.HasValue || right.Value == 0) return left; return Math.Min(left.Value, right.Value); }
        private static bool IsExactDate(string value) => !string.IsNullOrEmpty(value) && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }
}
