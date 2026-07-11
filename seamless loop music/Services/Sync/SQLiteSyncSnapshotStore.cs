using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    public class SQLiteSyncSnapshotStore : ISyncSnapshotStore
    {
        private readonly IDatabaseHelper _db;
        private readonly IPlaybackStatisticsSyncSnapshotAdapter _playbackStatisticsAdapter;

        public SQLiteSyncSnapshotStore(IDatabaseHelper db)
            : this(db, null)
        {
        }

        public SQLiteSyncSnapshotStore(IDatabaseHelper db, IPlaybackStatisticsSyncSnapshotAdapter playbackStatisticsAdapter = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _playbackStatisticsAdapter = playbackStatisticsAdapter ?? new PlaybackStatisticsSyncSnapshotAdapter(_db);
        }

        // ──────────────────────────────────────────
        //  Export
        // ──────────────────────────────────────────

        public async Task<SyncSnapshot> ExportSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                var deviceId = GetOrCreateDeviceId();
                var exportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                using var conn = _db.GetConnection();

                // ── Load all tracks ──
                var localTracks = conn.Query<ExportTrackDto>(@"
                    SELECT Id, FileName, FilePath, TotalSamples, DurationMs FROM Tracks
                ").ToList();

                var trackById = localTracks.ToDictionary(t => t.Id);

                // ── LoopPoints (only substantial) ──
                var loopPointRows = conn.Query<ExportLoopPointDto>(@"
                    SELECT TrackId, LoopStart, LoopEnd, AnalysisLastModified
                    FROM LoopPoints
                    WHERE (LoopStart != 0 OR LoopEnd != 0)
                      AND LoopStart IS NOT NULL
                      AND LoopEnd IS NOT NULL
                ").ToList();

                var loopPoints = new List<SyncLoopPointEntry>();
                foreach (var lp in loopPointRows)
                {
                    if (!trackById.TryGetValue(lp.TrackId, out var trk)) continue;
                    // Skip default full-track loops (0→TotalSamples = desktop unset)
                    if (lp.LoopStart == 0 && lp.LoopEnd == trk.TotalSamples && trk.TotalSamples > 0)
                        continue;
                    loopPoints.Add(new SyncLoopPointEntry
                    {
                        Song = BuildSongIdentity(trk),
                        LoopPoint = new SyncLoopPoint
                        {
                            LoopStart = lp.LoopStart,
                            LoopEnd = lp.LoopEnd,
                            LastModified = DbDateTimeToEpochMs(lp.AnalysisLastModified) ?? exportedAt
                        }
                    });
                }

                // ── Ratings (only non-zero) ──
                var ratingRows = conn.Query<ExportRatingDto>(@"
                    SELECT TrackId, Rating, LastModified
                    FROM UserRatings
                    WHERE Rating IS NOT NULL AND Rating != 0
                ").ToList();

                var ratings = new List<SyncRatingEntry>();
                foreach (var r in ratingRows)
                {
                    if (!trackById.TryGetValue(r.TrackId, out var trk)) continue;
                    ratings.Add(new SyncRatingEntry
                    {
                        Song = BuildSongIdentity(trk),
                        Rating = new SyncRating
                        {
                            RatingValue = r.Rating,
                            LastModified = DbDateTimeToEpochMs(r.LastModified) ?? exportedAt
                        }
                    });
                }

                // ── Playlists ──
                var playlists = conn.Query<ExportPlaylistDto>(@"
                    SELECT Id, Name, CreatedAt FROM Playlists ORDER BY SortOrder ASC
                ").ToList();

                var syncPlaylists = new List<SyncPlaylist>();
                foreach (var pl in playlists)
                {
                    var syncId = GetOrCreatePlaylistSyncId(conn, pl.Id);
                    var createdAt = DbDateTimeToEpochMs(pl.CreatedAt) ?? exportedAt;

                    var items = conn.Query<ExportPlaylistItemDto>(@"
                        SELECT pi.SongId, pi.SortOrder
                        FROM PlaylistItems pi
                        WHERE pi.PlaylistId = @Pid
                        ORDER BY pi.SortOrder ASC
                    ", new { Pid = pl.Id }).ToList();

                    var syncItems = new List<SyncPlaylistItem>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (!trackById.TryGetValue(items[i].SongId, out var trk)) continue;
                        syncItems.Add(new SyncPlaylistItem
                        {
                            Song = BuildSongIdentity(trk),
                            SortOrder = i // re-normalize from 0
                        });
                    }

                    syncPlaylists.Add(new SyncPlaylist
                    {
                        Id = syncId,
                        Name = pl.Name,
                        CreatedAt = createdAt,
                        ModifiedAt = createdAt, // use createdAt, not now
                        Items = syncItems
                    });
                }

                var snapshot = new SyncSnapshot
                {
                    DeviceId = deviceId,
                    ExportedAt = exportedAt,
                    Playlists = syncPlaylists,
                    LoopPoints = loopPoints,
                    Ratings = ratings
                };

                snapshot.SchemaVersion = 2;
                snapshot.PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Canonicalize(
                    _playbackStatisticsAdapter.Export(conn));

                return snapshot;
            });
        }

        // ──────────────────────────────────────────
        //  Apply
        // ──────────────────────────────────────────

        public async Task<SyncApplyResult> ApplySnapshotAsync(SyncSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.SchemaVersion != 2)
                throw new FormatException($"Unsupported schemaVersion: {snapshot.SchemaVersion}. Expected: 2.");

            return await Task.Run(() =>
            {
                var result = new SyncApplyResult();

                using var conn = _db.GetConnection();

                // ── Load all local tracks for matching ──
                var localTracks = conn.Query<LocalTrackDto>(@"
                    SELECT Id, FileName, TotalSamples, DurationMs
                    FROM Tracks
                ").ToList();

                // ── 1. Apply LoopPoints ──
                foreach (var entry in snapshot.LoopPoints ?? Enumerable.Empty<SyncLoopPointEntry>())
                {
                    if (entry.LoopPoint == null) continue;
                    if (entry.LoopPoint.LoopStart == 0 && entry.LoopPoint.LoopEnd == 0)
                        continue; // skip 0/0 (mobile-style unset)

                    var match = MatchTrack(entry.Song, localTracks);
                    if (match == null) { result.SkippedUnmatched++; continue; }
                    if (match.Track == null)
                    {
                        if (match.Conflicts > 1) result.SkippedAmbiguous++;
                        else result.SkippedUnmatched++;
                        continue;
                    }

                    var localTrack = match.Track;
                    var localId = localTrack.Id;

                    // Skip 0→TotalSamples (desktop-style unset / full-track default)
                    if (entry.LoopPoint.LoopStart == 0 &&
                        entry.LoopPoint.LoopEnd == localTrack.TotalSamples &&
                        localTrack.TotalSamples > 0)
                        continue;

                    // Check existing loop point
                    var existing = conn.Query<ExistingLoopPointDto>(@"
                        SELECT LoopStart, LoopEnd, AnalysisLastModified
                        FROM LoopPoints WHERE TrackId = @Id
                    ", new { Id = localId }).FirstOrDefault();

                    // Treat local 0→TotalSamples as unset (not substantial)
                    bool localSubstantial = existing != null &&
                        !(existing.LoopStart == 0 &&
                          existing.LoopEnd == localTrack.TotalSamples &&
                          localTrack.TotalSamples > 0);
                    long? localLastModified = existing != null ? DbDateTimeToEpochMs(existing.AnalysisLastModified) : null;

                    if (localSubstantial && localLastModified.HasValue &&
                        localLastModified.Value > entry.LoopPoint.LastModified)
                    {
                        result.SkippedLoopPoints++;
                        continue; // local is newer
                    }

                    // Upsert (don't write LoopCandidatesJson)
                    conn.Execute(@"
                        INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, AnalysisLastModified)
                        VALUES (@Id, @Start, @End, @Modified)
                        ON CONFLICT(TrackId) DO UPDATE SET
                            LoopStart = excluded.LoopStart,
                            LoopEnd = excluded.LoopEnd,
                            AnalysisLastModified = excluded.AnalysisLastModified
                    ", new
                    {
                        Id = localId,
                        Start = entry.LoopPoint.LoopStart,
                        End = entry.LoopPoint.LoopEnd,
                        Modified = EpochMsToDateTime(entry.LoopPoint.LastModified)
                    });

                    result.AppliedLoopPoints++;
                }

                // ── 2. Apply Ratings ──
                foreach (var entry in snapshot.Ratings ?? Enumerable.Empty<SyncRatingEntry>())
                {
                    if (entry.Rating == null) continue;
                    if (entry.Rating.RatingValue == 0) continue; // skip 0

                    var match = MatchTrack(entry.Song, localTracks);
                    if (match == null) { result.SkippedUnmatched++; continue; }
                    if (match.Track == null)
                    {
                        if (match.Conflicts > 1) result.SkippedAmbiguous++;
                        else result.SkippedUnmatched++;
                        continue;
                    }

                    var localId = match.Track.Id;

                    // Check existing rating
                    var existing = conn.Query<ExistingRatingDto>(@"
                        SELECT Rating, LastModified
                        FROM UserRatings WHERE TrackId = @Id
                    ", new { Id = localId }).FirstOrDefault();

                    bool localNonZero = existing != null && existing.Rating != 0;
                    long? localLastModified = existing != null ? DbDateTimeToEpochMs(existing.LastModified) : null;

                    if (localNonZero && localLastModified.HasValue &&
                        localLastModified.Value > entry.Rating.LastModified)
                    {
                        result.SkippedRatings++;
                        continue; // local is newer
                    }

                    conn.Execute(@"
                        INSERT INTO UserRatings (TrackId, Rating, LastModified)
                        VALUES (@Id, @Rating, @Modified)
                        ON CONFLICT(TrackId) DO UPDATE SET
                            Rating = excluded.Rating,
                            LastModified = excluded.LastModified
                    ", new
                    {
                        Id = localId,
                        Rating = entry.Rating.RatingValue,
                        Modified = EpochMsToDateTime(entry.Rating.LastModified)
                    });

                    result.AppliedRatings++;
                }

                // ── 3. Apply Playlists ──
                foreach (var syncPl in snapshot.Playlists ?? Enumerable.Empty<SyncPlaylist>())
                {
                    if (string.IsNullOrEmpty(syncPl.Id) || string.IsNullOrEmpty(syncPl.Name))
                        continue;

                    int localPlaylistId = ResolvePlaylistId(conn, syncPl);

                    // Update name
                    conn.Execute("UPDATE Playlists SET Name = @Name WHERE Id = @Id",
                        new { Id = localPlaylistId, Name = syncPl.Name });

                    // Clear existing items
                    conn.Execute("DELETE FROM PlaylistItems WHERE PlaylistId = @Id",
                        new { Id = localPlaylistId });

                    // Build and insert new items
                    var resolvedItems = new List<(int SongId, int SortOrder)>();
                    foreach (var item in syncPl.Items ?? Enumerable.Empty<SyncPlaylistItem>())
                    {
                        if (item.Song == null) continue;

                        var match = MatchTrack(item.Song, localTracks);
                        if (match == null) { result.SkippedUnmatched++; continue; }
                        if (match.Track == null)
                        {
                            if (match.Conflicts > 1) result.SkippedAmbiguous++;
                            else result.SkippedUnmatched++;
                            continue;
                        }

                        resolvedItems.Add((match.Track.Id, item.SortOrder));
                    }

                    // Re-sort by sortOrder then fileName then duration
                    var sortedItems = resolvedItems
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x =>
                        {
                            var t = localTracks.FirstOrDefault(lt => lt.Id == x.SongId);
                            return t?.FileName ?? "";
                        })
                        .ThenBy(x =>
                        {
                            var t = localTracks.FirstOrDefault(lt => lt.Id == x.SongId);
                            return t?.DurationMs ?? 0;
                        })
                        .Select((x, i) => new { x.SongId, SortOrder = i })
                        .ToList();

                    foreach (var si in sortedItems)
                    {
                        conn.Execute(
                            "INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@Pid, @Sid, @Order)",
                            new { Pid = localPlaylistId, Sid = si.SongId, Order = si.SortOrder });
                    }

                    result.AppliedPlaylists++;
                }

                if (snapshot.PlaybackStatistics != null)
                {
                    using (var statisticsTransaction = conn.BeginTransaction())
                    {
                        try
                        {
                            _playbackStatisticsAdapter.Apply(conn, statisticsTransaction, snapshot.PlaybackStatistics);
                            statisticsTransaction.Commit();
                        }
                        catch
                        {
                            try { statisticsTransaction.Rollback(); } catch { }
                            throw;
                        }
                    }

                    _playbackStatisticsAdapter.RelinkExactAndUniqueFuzzy();
                }

                return result;
            });
        }

        // ──────────────────────────────────────────
        //  Track matching (4-tier algorithm)
        // ──────────────────────────────────────────

        internal class MatchResult
        {
            public LocalTrackDto Track { get; set; }
            public int Conflicts { get; set; } // 0=not found, 1=unique, >1=ambiguous
        }

        internal static MatchResult MatchTrack(SyncSongIdentity remote, List<LocalTrackDto> localTracks)
        {
            if (remote == null || string.IsNullOrWhiteSpace(remote.FileName))
                return new MatchResult { Conflicts = 0 };

            var remoteFn = remote.FileName.Trim().ToLowerInvariant();

            // Tier 1: fileName.lower + durationMs exact (remote durationMs > 0)
            if (remote.DurationMs > 0)
            {
                var matches = localTracks
                    .Where(t => t.FileName.Trim().Equals(remoteFn, StringComparison.OrdinalIgnoreCase)
                                && t.DurationMs == remote.DurationMs)
                    .ToList();

                if (matches.Count == 1)
                    return new MatchResult { Track = matches[0], Conflicts = 1 };
                if (matches.Count > 1)
                    return new MatchResult { Conflicts = matches.Count };
            }

            // Tier 2: fileName.lower + totalSamples with tolerance ±10000
            // (remote totalSamples != null && >0 && local totalSamples != 0)
            if (remote.TotalSamples.HasValue && remote.TotalSamples.Value > 0)
            {
                var matches = localTracks
                    .Where(t => t.FileName.Trim().Equals(remoteFn, StringComparison.OrdinalIgnoreCase)
                                && t.TotalSamples != 0
                                && Math.Abs(t.TotalSamples - remote.TotalSamples.Value) <= 10000)
                    .ToList();

                if (matches.Count == 1)
                    return new MatchResult { Track = matches[0], Conflicts = 1 };
                if (matches.Count > 1)
                    return new MatchResult { Conflicts = matches.Count };
            }

            // Tier 3: same-name with |local.DurationMs - remote.durationMs| <= 200ms (remote durationMs > 0)
            if (remote.DurationMs > 0)
            {
                var matches = localTracks
                    .Where(t => t.FileName.Trim().Equals(remoteFn, StringComparison.OrdinalIgnoreCase)
                                && Math.Abs(t.DurationMs - remote.DurationMs) <= 200)
                    .ToList();

                if (matches.Count == 1)
                    return new MatchResult { Track = matches[0], Conflicts = 1 };
                if (matches.Count > 1)
                    return new MatchResult { Conflicts = matches.Count };
            }

            // Tier 4: same-name fallback (must be unique)
            var fallback = localTracks
                .Where(t => t.FileName.Trim().Equals(remoteFn, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fallback.Count == 1)
                return new MatchResult { Track = fallback[0], Conflicts = 1 };
            if (fallback.Count > 1)
                return new MatchResult { Conflicts = fallback.Count };

            return new MatchResult { Conflicts = 0 };
        }

        // ──────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────

        private string GetOrCreateDeviceId()
        {
            var existing = _db.GetSetting("Sync.DeviceId");
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            var newId = Guid.NewGuid().ToString("D").ToLowerInvariant();
            _db.SetSetting("Sync.DeviceId", newId);
            return newId;
        }

        private string GetOrCreatePlaylistSyncId(IDbConnection conn, int localPlaylistId)
        {
            var key = $"Sync.PlaylistId.{localPlaylistId}";
            var existing = _db.GetSetting(key);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            var uuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
            _db.SetSetting(key, uuid);
            _db.SetSetting($"Sync.PlaylistLocalId.{uuid}", localPlaylistId.ToString());
            return uuid;
        }

        private int ResolvePlaylistId(IDbConnection conn, SyncPlaylist syncPl)
        {
            // 1. Check Sync.PlaylistLocalId.{syncUuid}
            var localKey = $"Sync.PlaylistLocalId.{syncPl.Id}";
            var localIdStr = _db.GetSetting(localKey);

            if (!string.IsNullOrWhiteSpace(localIdStr) && int.TryParse(localIdStr, out int localId))
            {
                // Verify playlist still exists
                var exists = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Playlists WHERE Id = @Id",
                    new { Id = localId }) > 0;
                if (exists)
                    return localId;
            }

            // 2. Try exact name match
            var byName = conn.QueryFirstOrDefault<int?>(
                "SELECT Id FROM Playlists WHERE Name = @Name",
                new { Name = syncPl.Name });
            if (byName.HasValue)
            {
                // Save mapping
                _db.SetSetting(localKey, byName.Value.ToString());
                _db.SetSetting($"Sync.PlaylistId.{byName.Value}", syncPl.Id);
                return byName.Value;
            }

            // 3. Create new playlist
            var newId = conn.ExecuteScalar<int>(@"
                INSERT INTO Playlists (Name) VALUES (@Name);
                SELECT last_insert_rowid();
            ", new { Name = syncPl.Name });

            // Save mapping
            _db.SetSetting(localKey, newId.ToString());
            _db.SetSetting($"Sync.PlaylistId.{newId}", syncPl.Id);

            return newId;
        }

        private static SyncSongIdentity BuildSongIdentity(ExportTrackDto track)
        {
            return new SyncSongIdentity
            {
                FileName = track.FileName,
                DurationMs = track.DurationMs > 0 ? track.DurationMs : 0,
                TotalSamples = track.TotalSamples > 0 ? track.TotalSamples : (long?)null
            };
        }

        private static long? DbDateTimeToEpochMs(DateTime? dt)
        {
            if (dt == null) return null;
            try
            {
                return new DateTimeOffset(dt.Value).ToUnixTimeMilliseconds();
            }
            catch
            {
                return null;
            }
        }

        private static DateTime EpochMsToDateTime(long ms)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        }

        // ──────────────────────────────────────────
        //  Internal DTOs
        // ──────────────────────────────────────────

        private class ExportTrackDto
        {
            public int Id { get; set; }
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public long TotalSamples { get; set; }
            public long DurationMs { get; set; }
        }

        private class ExportLoopPointDto
        {
            public int TrackId { get; set; }
            public long LoopStart { get; set; }
            public long LoopEnd { get; set; }
            public DateTime? AnalysisLastModified { get; set; }
        }

        private class ExportRatingDto
        {
            public int TrackId { get; set; }
            public int Rating { get; set; }
            public DateTime? LastModified { get; set; }
        }

        private class ExportPlaylistDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime? CreatedAt { get; set; }
        }

        private class ExportPlaylistItemDto
        {
            public int SongId { get; set; }
            public int SortOrder { get; set; }
        }

        internal class LocalTrackDto
        {
            public int Id { get; set; }
            public string FileName { get; set; }
            public long TotalSamples { get; set; }
            public long DurationMs { get; set; }
        }

        private class ExistingLoopPointDto
        {
            public long LoopStart { get; set; }
            public long LoopEnd { get; set; }
            public DateTime? AnalysisLastModified { get; set; }
        }

        private class ExistingRatingDto
        {
            public int Rating { get; set; }
            public DateTime? LastModified { get; set; }
        }
    }
}
