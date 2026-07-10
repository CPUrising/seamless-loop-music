using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public class PlaybackStatisticsRepository : BaseRepository, IPlaybackStatisticsRepository
    {
        public PlaybackStatisticsRepository() : base(null) { EnsureSchema(); }
        public PlaybackStatisticsRepository(string customDbPath) : base(customDbPath) { EnsureSchema(); }

        private void EnsureSchema()
        {
            using (var db = GetConnection())
            {
                db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, TrackId INTEGER NOT NULL, PlayedAtUtc DATETIME NOT NULL, FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackSegments (SegmentId TEXT PRIMARY KEY, TrackId INTEGER NOT NULL, StartedAtUtcMs INTEGER NOT NULL, DurationMs INTEGER NOT NULL CHECK(DurationMs > 0), FOREIGN KEY(TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE);");
                db.Execute("CREATE INDEX IF NOT EXISTS idx_playbackhistory_trackid_playedatutc ON PlaybackHistory(TrackId, PlayedAtUtc);");
                db.Execute("CREATE INDEX IF NOT EXISTS idx_playbackhistory_playedatutc_trackid ON PlaybackHistory(PlayedAtUtc, TrackId);");
                db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksegments_startedatutcms_trackid ON PlaybackSegments(StartedAtUtcMs, TrackId);");
                db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksegments_trackid_startedatutcms ON PlaybackSegments(TrackId, StartedAtUtcMs);");
            }
        }

        public async Task RecordPlaybackSegmentAsync(string segmentId, int trackId, long startedAtUtcMs, long durationMs)
        {
            if (string.IsNullOrWhiteSpace(segmentId)) throw new ArgumentException("A segment id is required.", nameof(segmentId));
            if (trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
            if (startedAtUtcMs < 0) throw new ArgumentOutOfRangeException(nameof(startedAtUtcMs));
            if (durationMs <= 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (durationMs > long.MaxValue - startedAtUtcMs) throw new ArgumentOutOfRangeException(nameof(durationMs));
            using (var db = GetConnection())
                await db.ExecuteAsync("INSERT OR IGNORE INTO PlaybackSegments (SegmentId, TrackId, StartedAtUtcMs, DurationMs) VALUES (@SegmentId, @TrackId, @StartedAtUtcMs, @DurationMs);", new { SegmentId = segmentId, TrackId = trackId, StartedAtUtcMs = startedAtUtcMs, DurationMs = durationMs });
        }

        public Task<int> ClearAllAsync()
        {
            using (var db = GetConnection())
            using (var transaction = db.BeginTransaction())
            {
                try
                {
                    var affected = db.Execute("DELETE FROM PlaybackSegments;", transaction: transaction);
                    affected += db.Execute("DELETE FROM PlaybackHistory;", transaction: transaction);
                    transaction.Commit();
                    return Task.FromResult(affected);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public async Task<List<PlaybackStatisticItem>> GetTopTracksAsync(PlaybackStatisticsPeriod period, int limit = 5, DateTime? nowLocal = null)
        {
            if (limit <= 0) return new List<PlaybackStatisticItem>();
            var now = nowLocal ?? DateTime.Now;
            var nowUtcMs = new DateTimeOffset(now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime()).ToUnixTimeMilliseconds();
            var fromUtcMs = GetPeriodStartUtcMs(period, now);
            const string sql = @"
                WITH Overlaps AS (
                    SELECT TrackId,
                           CASE WHEN StartedAtUtcMs < @FromUtcMs THEN @FromUtcMs ELSE StartedAtUtcMs END AS OverlapStart,
                           CASE WHEN StartedAtUtcMs + DurationMs > @NowUtcMs THEN @NowUtcMs ELSE StartedAtUtcMs + DurationMs END AS OverlapEnd,
                           StartedAtUtcMs
                    FROM PlaybackSegments
                    WHERE StartedAtUtcMs < @NowUtcMs AND (@FromUtcMs IS NULL OR StartedAtUtcMs + DurationMs > @FromUtcMs)
                )
                SELECT o.TrackId, COALESCE(NULLIF(t.DisplayName, ''), t.FileName) AS Title,
                       COALESCE(ar.Name, '') AS Artist, COALESCE(al.Name, '') AS Album,
                       t.CoverPath AS TrackCoverPath, al.CoverPath AS AlbumCoverPath, ar.CoverPath AS ArtistCoverPath,
                       SUM(o.OverlapEnd - o.OverlapStart) AS TotalDurationMs
                FROM Overlaps o INNER JOIN Tracks t ON t.Id = o.TrackId
                LEFT JOIN Artists ar ON ar.Id = t.ArtistId LEFT JOIN Albums al ON al.Id = t.AlbumId
                WHERE o.OverlapEnd > o.OverlapStart
                GROUP BY o.TrackId, t.DisplayName, t.FileName, ar.Name, al.Name, t.CoverPath, al.CoverPath, ar.CoverPath
                ORDER BY TotalDurationMs DESC, MAX(o.StartedAtUtcMs) DESC LIMIT @Limit;";
            using (var db = GetConnection())
                return (await db.QueryAsync<PlaybackStatisticItem>(sql, new { FromUtcMs = fromUtcMs, NowUtcMs = nowUtcMs, Limit = limit })).ToList();
        }

        private static long? GetPeriodStartUtcMs(PlaybackStatisticsPeriod period, DateTime nowLocal)
        {
            var local = nowLocal.Kind == DateTimeKind.Utc ? nowLocal.ToLocalTime() : nowLocal;
            DateTime start;
            switch (period)
            {
                case PlaybackStatisticsPeriod.Day: start = local.Date; break;
                case PlaybackStatisticsPeriod.Week: start = local.Date.AddDays(-((7 + ((int)local.DayOfWeek - (int)DayOfWeek.Monday) % 7) % 7)); break;
                case PlaybackStatisticsPeriod.Month: start = new DateTime(local.Year, local.Month, 1); break;
                case PlaybackStatisticsPeriod.Year: start = new DateTime(local.Year, 1, 1); break;
                case PlaybackStatisticsPeriod.All: return null;
                default: throw new ArgumentOutOfRangeException(nameof(period));
            }
            return new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        }
    }
}
