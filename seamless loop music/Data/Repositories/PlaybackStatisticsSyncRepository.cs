using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dapper;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync;

namespace seamless_loop_music.Data.Repositories
{
    public sealed class PlaybackStatisticsSyncRepository : BaseRepository, IPlaybackStatisticsSyncRepository
    {
        public PlaybackStatisticsSyncRepository() : base(null) { }
        public PlaybackStatisticsSyncRepository(string customDbPath) : base(customDbPath) { }

        public PlaybackSyncDevice EnsureDevice(PlaybackSyncDevice device)
        {
            return MergeDevice(device, false);
        }

        public PlaybackSyncDevice EnsureLocalDevice(string deviceId, string displayName, long seenAtUtcMs)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(displayName) || seenAtUtcMs <= 0)
                throw new ArgumentOutOfRangeException();

            displayName = displayName.Trim();
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var existing = db.QueryFirstOrDefault<PlaybackSyncDevice>(
                    "SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId",
                    new { DeviceId = deviceId });

                if (existing == null)
                {
                    var device = new PlaybackSyncDevice
                    {
                        DeviceId = deviceId,
                        CurrentGeneration = 0,
                        DisplayName = displayName,
                        DisplayNameUpdatedAtUtcMs = seenAtUtcMs,
                        Platform = "windows",
                        FirstSeenAtUtcMs = seenAtUtcMs,
                        LastSeenAtUtcMs = seenAtUtcMs
                    };
                    db.Execute("INSERT INTO PlaybackSyncDevices VALUES (@DeviceId,@CurrentGeneration,@DisplayName,@DisplayNameUpdatedAtUtcMs,@Platform,@FirstSeenAtUtcMs,@LastSeenAtUtcMs)", device);
                    return device;
                }

                var needsRepair = string.IsNullOrWhiteSpace(existing.DisplayName) || existing.DisplayNameUpdatedAtUtcMs <= 0;
                if (needsRepair)
                {
                    if (string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = displayName;
                    existing.DisplayNameUpdatedAtUtcMs = seenAtUtcMs;
                }

                existing.FirstSeenAtUtcMs = Math.Min(existing.FirstSeenAtUtcMs, seenAtUtcMs);
                existing.LastSeenAtUtcMs = Math.Max(existing.LastSeenAtUtcMs, seenAtUtcMs);
                db.Execute("UPDATE PlaybackSyncDevices SET DisplayName=@DisplayName, DisplayNameUpdatedAtUtcMs=@DisplayNameUpdatedAtUtcMs, FirstSeenAtUtcMs=@FirstSeenAtUtcMs, LastSeenAtUtcMs=@LastSeenAtUtcMs WHERE DeviceId=@DeviceId", existing);
                return existing;
            }
        }

        // Only a future v2 apply of this device's owner-authored registration may call this method.
        public PlaybackSyncDevice MergeOwnerAuthoredDeviceRegistration(PlaybackSyncDevice device)
        {
            return MergeDevice(device, true);
        }

        public PlaybackSyncDevice AdvanceLocalDeviceGeneration(string deviceId, long generation, long seenAtUtcMs)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || generation < 0 || seenAtUtcMs < 0) throw new ArgumentOutOfRangeException();
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var affected = db.Execute("UPDATE PlaybackSyncDevices SET CurrentGeneration=@Generation, LastSeenAtUtcMs=CASE WHEN LastSeenAtUtcMs>@Seen THEN LastSeenAtUtcMs ELSE @Seen END WHERE DeviceId=@DeviceId AND CurrentGeneration<=@Generation", new { DeviceId = deviceId, Generation = generation, Seen = seenAtUtcMs });
                if (affected == 0 && db.ExecuteScalar<long>("SELECT COUNT(*) FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = deviceId }) == 0) throw new InvalidOperationException("Register the local device before advancing its generation.");
                return db.QuerySingle<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = deviceId });
            }
        }

        public PlaybackSyncSong EnsureSong(PlaybackSyncSong song)
        {
            ValidateAndNormalizeSong(song);
            if (song.LocalTrackId.HasValue) return EnsureSongBoundToTrack(song, song.LocalTrackId.Value);
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var existing = db.QueryFirstOrDefault<PlaybackSyncSong>("SELECT * FROM PlaybackSyncSongs WHERE NormalizedFileName=@NormalizedFileName AND DurationMs=@DurationMs", song);
                if (existing == null)
                {
                    db.Execute(@"INSERT INTO PlaybackSyncSongs (NormalizedFileName,FileName,DurationMs,TotalSamples,ContentHash,LocalTrackId) VALUES (@NormalizedFileName,@FileName,@DurationMs,@TotalSamples,@ContentHash,@LocalTrackId)", song);
                    song.SongId = db.ExecuteScalar<long>("SELECT last_insert_rowid()"); return song;
                }
                // Diagnostics converge independently of arrival order.
                var fileName = string.CompareOrdinal(song.FileName, existing.FileName) > 0 ? song.FileName : existing.FileName;
                var samples = Max(existing.TotalSamples, song.TotalSamples);
                var hash = string.CompareOrdinal(song.ContentHash ?? "", existing.ContentHash ?? "") > 0 ? song.ContentHash : existing.ContentHash;
                db.Execute("UPDATE PlaybackSyncSongs SET FileName=@fileName, TotalSamples=@samples, ContentHash=@hash WHERE SongId=@SongId", new { fileName, samples, hash, existing.SongId });
                existing.FileName = fileName; existing.TotalSamples = samples; existing.ContentHash = hash; return existing;
            }
        }

        public PlaybackSyncSong EnsureSongBoundToTrack(PlaybackSyncSong song, int localTrackId)
        {
            ValidateAndNormalizeSong(song);
            if (localTrackId <= 0) throw new ArgumentOutOfRangeException(nameof(localTrackId));

            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    var track = db.QueryFirstOrDefault<TrackIdentity>("SELECT Id,FileName,DurationMs FROM Tracks WHERE Id=@Id", new { Id = localTrackId });
                    if (track == null) throw new InvalidOperationException("The referenced local track does not exist.");
                    var normalizedTrackName = SyncSnapshotSerializer.NormalizePlaybackSongFileName(track.FileName);
                    if (!string.Equals(normalizedTrackName, song.NormalizedFileName, StringComparison.Ordinal))
                        throw new InvalidOperationException("The local track does not match the playback song identity.");

                    var existing = db.QueryFirstOrDefault<PlaybackSyncSong>("SELECT * FROM PlaybackSyncSongs WHERE NormalizedFileName=@NormalizedFileName AND DurationMs=@DurationMs", song);
                    if (existing == null)
                    {
                        db.Execute(@"INSERT INTO PlaybackSyncSongs (NormalizedFileName,FileName,DurationMs,TotalSamples,ContentHash,LocalTrackId)
                                     VALUES (@NormalizedFileName,@FileName,@DurationMs,@TotalSamples,@ContentHash,@LocalTrackId)", new
                        {
                            song.NormalizedFileName,
                            song.FileName,
                            song.DurationMs,
                            song.TotalSamples,
                            song.ContentHash,
                            LocalTrackId = localTrackId
                        });
                        song.SongId = db.ExecuteScalar<long>("SELECT last_insert_rowid()");
                        existing = song;
                    }
                    else
                    {
                        MergeSongDiagnostics(db, existing, song);
                    }

                    db.Execute("UPDATE PlaybackSyncSongs SET LocalTrackId=@LocalTrackId WHERE SongId=@SongId", new { LocalTrackId = localTrackId, existing.SongId });
                    existing.LocalTrackId = localTrackId;
                    db.Execute("COMMIT;");
                    return existing;
                }
                catch
                {
                    try { db.Execute("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        public void MergeContribution(PlaybackSyncContribution contribution)
        {
            ValidateContribution(contribution);
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                using (var tx = db.BeginTransaction())
                {
                    var old = db.QueryFirstOrDefault<PlaybackSyncContribution>("SELECT * FROM PlaybackSyncContributions WHERE SongId=@SongId AND DeviceId=@DeviceId AND Generation=@Generation", contribution, tx);
                    if (old == null) db.Execute(@"INSERT INTO PlaybackSyncContributions VALUES (@SongId,@DeviceId,@Generation,@UndatedListenMs,@FirstPlayedAtUtcMs,@LastPlayedAtUtcMs,@UpdatedAtUtcMs)", contribution, tx);
                    else db.Execute(@"UPDATE PlaybackSyncContributions SET UndatedListenMs=CASE WHEN UndatedListenMs>@Undated THEN UndatedListenMs ELSE @Undated END, FirstPlayedAtUtcMs=@First, LastPlayedAtUtcMs=@Last, UpdatedAtUtcMs=CASE WHEN UpdatedAtUtcMs>@Updated THEN UpdatedAtUtcMs ELSE @Updated END WHERE SongId=@SongId AND DeviceId=@DeviceId AND Generation=@Generation", new { contribution.SongId, contribution.DeviceId, contribution.Generation, Undated = contribution.UndatedListenMs, First = MinNonZero(old.FirstPlayedAtUtcMs, contribution.FirstPlayedAtUtcMs), Last = Max(old.LastPlayedAtUtcMs, contribution.LastPlayedAtUtcMs), Updated = contribution.UpdatedAtUtcMs }, tx);
                    foreach (var bucket in contribution.DailyBuckets ?? new List<PlaybackSyncDailyBucket>())
                    {
                        ValidateDate(bucket.LocalDate);
                        db.Execute(@"INSERT INTO PlaybackSyncDailyBuckets VALUES (@SongId,@DeviceId,@Generation,@LocalDate,@ListenMs) ON CONFLICT(SongId,DeviceId,Generation,LocalDate) DO UPDATE SET ListenMs=CASE WHEN ListenMs>excluded.ListenMs THEN ListenMs ELSE excluded.ListenMs END", new { contribution.SongId, contribution.DeviceId, contribution.Generation, bucket.LocalDate, bucket.ListenMs }, tx);
                    }
                    tx.Commit();
                }
            }
        }

        public void InsertTombstone(PlaybackSyncTombstone tombstone)
        {
            if (tombstone == null || string.IsNullOrWhiteSpace(tombstone.DeviceId) || string.IsNullOrWhiteSpace(tombstone.TombstonedByDeviceId) || string.IsNullOrWhiteSpace(tombstone.Reason) || tombstone.Generation < 0 || tombstone.TombstonedAtUtcMs < 0 || tombstone.Scope != "deviceGeneration") throw new ArgumentException("Invalid tombstone.", nameof(tombstone));
            using (var db = GetConnection()) { PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("INSERT OR IGNORE INTO PlaybackSyncTombstones VALUES (@DeviceId,@Generation,@Scope,@TombstonedAtUtcMs,@TombstonedByDeviceId,@Reason)", tombstone); }
        }

        public bool RecordSettlement(PlaybackSyncSettlement settlement, long listenMs, string localDate, long? firstPlayedAtUtcMs, long? lastPlayedAtUtcMs)
        {
            if (settlement == null || string.IsNullOrWhiteSpace(settlement.SettlementEventId) || string.IsNullOrWhiteSpace(settlement.SourceKind) || settlement.SongId <= 0 || string.IsNullOrWhiteSpace(settlement.DeviceId) || settlement.Generation < 0 || settlement.AppliedAtUtcMs < 0 || listenMs < 0) throw new ArgumentException("Invalid settlement.");
            if (localDate != null) ValidateDate(localDate);
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    if (!PlaybackStatisticsSyncPersistence.ConsumeSettlement(db, null, settlement, listenMs, localDate, firstPlayedAtUtcMs, lastPlayedAtUtcMs)) { db.Execute("COMMIT;"); return false; }
                    db.Execute("COMMIT;"); return true;
                }
                catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
            }
        }

        public PlaybackStatisticsGenerationClearResult TombstoneAndRotateLocalGeneration(string deviceId, long tombstonedAtUtcMs)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || tombstonedAtUtcMs < 0) throw new ArgumentOutOfRangeException();
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    var oldGeneration = db.QuerySingle<long>("SELECT CurrentGeneration FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = deviceId });
                    var affected = db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId AND Generation=@Generation", new { DeviceId = deviceId, Generation = oldGeneration });
                    db.Execute("INSERT OR IGNORE INTO PlaybackSyncTombstones VALUES (@DeviceId,@Generation,'deviceGeneration',@At,@DeviceId,'localClear')", new { DeviceId = deviceId, Generation = oldGeneration, At = tombstonedAtUtcMs });
                    db.Execute("DELETE FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId AND Generation=@Generation", new { DeviceId = deviceId, Generation = oldGeneration });
                    var maximum = db.QuerySingle<long?>(@"SELECT MAX(Generation) FROM (
                        SELECT CurrentGeneration AS Generation FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId
                        UNION ALL SELECT Generation FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId
                        UNION ALL SELECT Generation FROM PlaybackSyncTombstones WHERE DeviceId=@DeviceId
                        UNION ALL SELECT Generation FROM PlaybackStatisticsSettlements WHERE DeviceId=@DeviceId)", new { DeviceId = deviceId }) ?? 0;
                    if (maximum == long.MaxValue) throw new OverflowException("Cannot rotate playback statistics generation beyond Int64.MaxValue.");
                    var next = maximum + 1;
                    db.Execute("UPDATE PlaybackSyncDevices SET CurrentGeneration=@Next, LastSeenAtUtcMs=CASE WHEN LastSeenAtUtcMs>@At THEN LastSeenAtUtcMs ELSE @At END WHERE DeviceId=@DeviceId", new { DeviceId = deviceId, Next = next, At = tombstonedAtUtcMs });
                    db.Execute("COMMIT;");
                    return new PlaybackStatisticsGenerationClearResult { OldGeneration = oldGeneration, NewGeneration = next, AffectedContributionCount = affected };
                }
                catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
            }
        }

        public PlaybackStatisticsTombstoneObservationResult ObserveCurrentGenerationTombstone(string deviceId, long observedGeneration, long observedAtUtcMs)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || observedGeneration < 0 || observedAtUtcMs < 0) throw new ArgumentOutOfRangeException();
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    var device = db.QueryFirstOrDefault<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = deviceId });
                    if (device == null || device.CurrentGeneration != observedGeneration)
                    {
                        db.Execute("COMMIT;");
                        return new PlaybackStatisticsTombstoneObservationResult { DeviceId = deviceId, OldGeneration = observedGeneration, NewGeneration = device == null ? observedGeneration : device.CurrentGeneration };
                    }
                    var tombstoned = db.ExecuteScalar<long>("SELECT COUNT(*) FROM PlaybackSyncTombstones WHERE DeviceId=@DeviceId AND Generation=@Generation AND Scope='deviceGeneration'", new { DeviceId = deviceId, Generation = observedGeneration }) != 0;
                    if (!tombstoned)
                    {
                        db.Execute("COMMIT;");
                        return new PlaybackStatisticsTombstoneObservationResult { DeviceId = deviceId, OldGeneration = observedGeneration, NewGeneration = observedGeneration };
                    }
                    var affected = db.ExecuteScalar<int>("SELECT COUNT(*) FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId AND Generation=@Generation", new { DeviceId = deviceId, Generation = observedGeneration });
                    db.Execute("DELETE FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId AND Generation=@Generation", new { DeviceId = deviceId, Generation = observedGeneration });
                    var maximum = MaxKnownGeneration(db, deviceId);
                    if (maximum == long.MaxValue) throw new OverflowException("Cannot rotate playback statistics generation beyond Int64.MaxValue.");
                    var next = maximum + 1;
                    db.Execute("UPDATE PlaybackSyncDevices SET CurrentGeneration=@Next, LastSeenAtUtcMs=CASE WHEN LastSeenAtUtcMs>@At THEN LastSeenAtUtcMs ELSE @At END WHERE DeviceId=@DeviceId", new { DeviceId = deviceId, Next = next, At = observedAtUtcMs });
                    db.Execute("COMMIT;");
                    return new PlaybackStatisticsTombstoneObservationResult { Rotated = true, DeviceId = deviceId, OldGeneration = observedGeneration, NewGeneration = next, AffectedContributionCount = affected };
                }
                catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
            }
        }

        public int RelinkSongs()
        {
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    var linked = PlaybackStatisticsSyncPersistence.RelinkSongs(db, null);
                    db.Execute("COMMIT;"); return linked;
                }
                catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
            }
        }

        public IReadOnlyList<PlaybackStatisticsSourceDevice> GetSourceDevices(string localDeviceId)
        {
            if (string.IsNullOrWhiteSpace(localDeviceId)) throw new ArgumentException("A local device is required.", nameof(localDeviceId));
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var devices = db.Query<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices ORDER BY DeviceId").ToList();
                var tombstones = new HashSet<string>(db.Query<PlaybackSyncTombstone>("SELECT * FROM PlaybackSyncTombstones").Select(x => x.DeviceId + "|" + x.Generation));
                var linked = new HashSet<long>(db.Query<long>("SELECT s.SongId FROM PlaybackSyncSongs s JOIN Tracks t ON t.Id=s.LocalTrackId"));
                var contributions = db.Query<PlaybackSyncContribution>("SELECT * FROM PlaybackSyncContributions").ToList();
                var buckets = db.Query<PlaybackSyncDailyBucket>("SELECT * FROM PlaybackSyncDailyBuckets").ToList();
                var settlements = db.Query<PlaybackSyncSettlement>("SELECT * FROM PlaybackStatisticsSettlements").ToList();
                return devices.Select(device =>
                {
                    var prefix = device.DeviceId + "|";
                    var active = new HashSet<long> { device.CurrentGeneration };
                    foreach (var value in contributions.Where(x => x.DeviceId == device.DeviceId)) active.Add(value.Generation);
                    foreach (var value in settlements.Where(x => x.DeviceId == device.DeviceId)) active.Add(value.Generation);
                    active.RemoveWhere(generation => tombstones.Contains(prefix + generation));
                    long total = 0;
                    foreach (var contribution in contributions.Where(x => x.DeviceId == device.DeviceId && linked.Contains(x.SongId) && !tombstones.Contains(prefix + x.Generation)))
                    {
                        total = Saturating(total, contribution.UndatedListenMs);
                        foreach (var bucket in buckets.Where(x => x.SongId == contribution.SongId && x.DeviceId == contribution.DeviceId && x.Generation == contribution.Generation)) total = Saturating(total, bucket.ListenMs);
                    }
                    return new PlaybackStatisticsSourceDevice { DeviceId = device.DeviceId, DisplayName = device.DisplayName, Platform = device.Platform, IsLocalDevice = device.DeviceId == localDeviceId, CurrentGeneration = device.CurrentGeneration, EffectiveTotalListenMs = total, KnownActiveGenerationCount = active.Count };
                }).ToList();
            }
        }

        public PlaybackSyncDevice RenameDevice(string deviceId, string displayName, long updatedAtUtcMs)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || updatedAtUtcMs < 0) throw new ArgumentOutOfRangeException();
            displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    var device = db.QuerySingleOrDefault<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = deviceId });
                    if (device == null) throw new InvalidOperationException("Register the device before renaming it.");
                    if (updatedAtUtcMs > device.DisplayNameUpdatedAtUtcMs || (updatedAtUtcMs == device.DisplayNameUpdatedAtUtcMs && string.CompareOrdinal(displayName ?? "", device.DisplayName ?? "") > 0))
                    {
                        device.DisplayName = displayName; device.DisplayNameUpdatedAtUtcMs = updatedAtUtcMs;
                        db.Execute("UPDATE PlaybackSyncDevices SET DisplayName=@DisplayName, DisplayNameUpdatedAtUtcMs=@DisplayNameUpdatedAtUtcMs WHERE DeviceId=@DeviceId", device);
                    }
                    db.Execute("COMMIT;"); return device;
                }
                catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
            }
        }

        public int TombstoneKnownActiveGenerations(IEnumerable<string> deviceIds, long tombstonedAtUtcMs, string actorDeviceId, string reason, string localDeviceId)
        {
            if (deviceIds == null || string.IsNullOrWhiteSpace(actorDeviceId) || string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(localDeviceId) || tombstonedAtUtcMs < 0) throw new ArgumentException("Invalid tombstone request.");
            var targets = deviceIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db); db.Execute("BEGIN IMMEDIATE;");
                try
                {
                    if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = actorDeviceId }) == 0) throw new InvalidOperationException("Register the actor device before tombstoning.");
                    var inserted = 0;
                    foreach (var target in targets)
                    {
                        if (target == localDeviceId) continue;
                        if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", new { DeviceId = target }) == 0) throw new InvalidOperationException("Register every target device before tombstoning.");
                        foreach (var generation in KnownActiveGenerations(db, target))
                            inserted += db.Execute("INSERT OR IGNORE INTO PlaybackSyncTombstones VALUES (@DeviceId,@Generation,'deviceGeneration',@At,@Actor,@Reason)", new { DeviceId = target, Generation = generation, At = tombstonedAtUtcMs, Actor = actorDeviceId, Reason = reason });
                    }
                    db.Execute("COMMIT;"); return inserted;
                }
                catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
            }
        }

        public int TombstoneAllKnownNonLocalGenerations(long tombstonedAtUtcMs, string actorDeviceId, string reason, string localDeviceId)
        {
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var targets = db.Query<string>("SELECT DeviceId FROM PlaybackSyncDevices WHERE DeviceId<>@Local ORDER BY DeviceId", new { Local = localDeviceId }).ToList();
                return TombstoneKnownActiveGenerations(targets, tombstonedAtUtcMs, actorDeviceId, reason, localDeviceId);
            }
        }

        public PlaybackSyncPersistedState LoadState()
        {
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var state = new PlaybackSyncPersistedState { Devices = db.Query<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices ORDER BY DeviceId").ToList(), Songs = db.Query<PlaybackSyncSong>("SELECT * FROM PlaybackSyncSongs ORDER BY SongId").ToList(), Contributions = db.Query<PlaybackSyncContribution>("SELECT * FROM PlaybackSyncContributions ORDER BY SongId,DeviceId,Generation").ToList(), Tombstones = db.Query<PlaybackSyncTombstone>("SELECT * FROM PlaybackSyncTombstones ORDER BY DeviceId,Generation,Scope").ToList() };
                var buckets = db.Query<PlaybackSyncDailyBucket>("SELECT * FROM PlaybackSyncDailyBuckets ORDER BY SongId,DeviceId,Generation,LocalDate").ToList(); foreach (var contribution in state.Contributions) contribution.DailyBuckets = buckets.Where(x => x.SongId == contribution.SongId && x.DeviceId == contribution.DeviceId && x.Generation == contribution.Generation).ToList(); return state;
            }
        }

        private static void ValidateAndNormalizeSong(PlaybackSyncSong song)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.FileName) || song.DurationMs < 0 || (song.TotalSamples.HasValue && song.TotalSamples.Value < 0)) throw new ArgumentOutOfRangeException(nameof(song));
            song.NormalizedFileName = SyncSnapshotSerializer.NormalizePlaybackSongFileName(song.FileName);
            if (string.IsNullOrWhiteSpace(song.NormalizedFileName)) throw new ArgumentException("A normalized filename is required.", nameof(song));
        }

        private static void MergeSongDiagnostics(System.Data.IDbConnection db, PlaybackSyncSong existing, PlaybackSyncSong incoming)
        {
            var fileName = string.CompareOrdinal(incoming.FileName, existing.FileName) > 0 ? incoming.FileName : existing.FileName;
            var samples = Max(existing.TotalSamples, incoming.TotalSamples);
            var hash = string.CompareOrdinal(incoming.ContentHash ?? "", existing.ContentHash ?? "") > 0 ? incoming.ContentHash : existing.ContentHash;
            db.Execute("UPDATE PlaybackSyncSongs SET FileName=@fileName, TotalSamples=@samples, ContentHash=@hash WHERE SongId=@SongId", new { fileName, samples, hash, existing.SongId });
            existing.FileName = fileName;
            existing.TotalSamples = samples;
            existing.ContentHash = hash;
        }

        private sealed class TrackIdentity
        {
            public int Id { get; set; }
            public string FileName { get; set; }
            public long DurationMs { get; set; }
        }

        private static long MaxKnownGeneration(System.Data.IDbConnection db, string deviceId) => db.QuerySingle<long?>(@"SELECT MAX(Generation) FROM (
            SELECT CurrentGeneration AS Generation FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId
            UNION ALL SELECT Generation FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId
            UNION ALL SELECT Generation FROM PlaybackSyncTombstones WHERE DeviceId=@DeviceId
            UNION ALL SELECT Generation FROM PlaybackStatisticsSettlements WHERE DeviceId=@DeviceId)", new { DeviceId = deviceId }) ?? 0;
        private static IEnumerable<long> KnownActiveGenerations(System.Data.IDbConnection db, string deviceId) => db.Query<long?>(@"SELECT Generation FROM (
            SELECT CurrentGeneration AS Generation FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId
            UNION SELECT Generation FROM PlaybackSyncContributions WHERE DeviceId=@DeviceId
            UNION SELECT Generation FROM PlaybackStatisticsSettlements WHERE DeviceId=@DeviceId)
            EXCEPT SELECT Generation FROM PlaybackSyncTombstones WHERE DeviceId=@DeviceId", new { DeviceId = deviceId }).Where(x => x.HasValue).Select(x => x.Value);
        private static long Saturating(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
        private static long? MinNonZero(long? a, long? b) { if (!a.HasValue || a.Value == 0) return b; if (!b.HasValue || b.Value == 0) return a; return Math.Min(a.Value, b.Value); }
        private static long? Max(long? a, long? b) => !a.HasValue ? b : !b.HasValue ? a : Math.Max(a.Value, b.Value);
        private PlaybackSyncDevice MergeDevice(PlaybackSyncDevice device, bool allowGenerationAdvance)
        {
            ValidateDevice(device);
            using (var db = GetConnection())
            {
                PlaybackStatisticsSyncSchema.EnsureSchema(db);
                var existing = db.QueryFirstOrDefault<PlaybackSyncDevice>("SELECT * FROM PlaybackSyncDevices WHERE DeviceId=@DeviceId", device);
                if (existing == null)
                {
                    db.Execute(@"INSERT INTO PlaybackSyncDevices VALUES (@DeviceId,@CurrentGeneration,@DisplayName,@DisplayNameUpdatedAtUtcMs,@Platform,@FirstSeenAtUtcMs,@LastSeenAtUtcMs)", device);
                    return device;
                }
                var incomingWins = device.DisplayNameUpdatedAtUtcMs > existing.DisplayNameUpdatedAtUtcMs ||
                    (device.DisplayNameUpdatedAtUtcMs == existing.DisplayNameUpdatedAtUtcMs && string.CompareOrdinal(device.DisplayName ?? "", existing.DisplayName ?? "") > 0);
                existing.FirstSeenAtUtcMs = Math.Min(existing.FirstSeenAtUtcMs, device.FirstSeenAtUtcMs);
                existing.LastSeenAtUtcMs = Math.Max(existing.LastSeenAtUtcMs, device.LastSeenAtUtcMs);
                if (incomingWins) { existing.DisplayName = device.DisplayName; existing.DisplayNameUpdatedAtUtcMs = device.DisplayNameUpdatedAtUtcMs; }
                if (allowGenerationAdvance) existing.CurrentGeneration = Math.Max(existing.CurrentGeneration, device.CurrentGeneration);
                db.Execute(@"UPDATE PlaybackSyncDevices SET CurrentGeneration=@CurrentGeneration, DisplayName=@DisplayName, DisplayNameUpdatedAtUtcMs=@DisplayNameUpdatedAtUtcMs, FirstSeenAtUtcMs=@FirstSeenAtUtcMs, LastSeenAtUtcMs=@LastSeenAtUtcMs WHERE DeviceId=@DeviceId", existing);
                return existing;
            }
        }
        private static void ValidateDate(string date) { if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("LocalDate must be yyyy-MM-dd."); }
        private static void ValidateDevice(PlaybackSyncDevice device) { if (device == null || string.IsNullOrWhiteSpace(device.DeviceId) || device.CurrentGeneration < 0 || device.DisplayNameUpdatedAtUtcMs < 0 || device.FirstSeenAtUtcMs < 0 || device.LastSeenAtUtcMs < device.FirstSeenAtUtcMs || (device.Platform != "android" && device.Platform != "windows")) throw new ArgumentException("Invalid device."); }
        private static void ValidateContribution(PlaybackSyncContribution value) { if (value == null || value.SongId <= 0 || string.IsNullOrWhiteSpace(value.DeviceId) || value.Generation < 0 || value.UndatedListenMs < 0 || value.UpdatedAtUtcMs < 0 || value.FirstPlayedAtUtcMs < 0 || value.LastPlayedAtUtcMs < 0 || (value.FirstPlayedAtUtcMs.HasValue && value.LastPlayedAtUtcMs.HasValue && value.FirstPlayedAtUtcMs > value.LastPlayedAtUtcMs) || (value.DailyBuckets ?? new List<PlaybackSyncDailyBucket>()).Any(x => x.ListenMs < 0)) throw new ArgumentException("Invalid contribution."); }
    }
}
