using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using seamless_loop_music.Models;
using seamless_loop_music.Services.Sync;

namespace seamless_loop_music.Data.Repositories
{
    internal static class PlaybackStatisticsSyncPersistence
    {
        internal static bool HasDeviceGenerationTombstone(IDbConnection db, IDbTransaction tx, string deviceId, long generation)
        {
            return db.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM PlaybackSyncTombstones WHERE DeviceId=@DeviceId AND Generation=@Generation AND Scope='deviceGeneration'",
                new { DeviceId = deviceId, Generation = generation },
                tx) != 0;
        }

        internal static bool ConsumeSettlement(IDbConnection db, IDbTransaction tx, PlaybackSyncSettlement settlement, long listenMs, string localDate, long? firstPlayedAtUtcMs, long? lastPlayedAtUtcMs)
        {
            if (db.Execute("INSERT OR IGNORE INTO PlaybackStatisticsSettlements VALUES (@SettlementEventId,@SongId,@DeviceId,@Generation,@AppliedAtUtcMs,@SourceKind,@Diagnostics)", settlement, tx) == 0)
                return false;
            if (!HasDeviceGenerationTombstone(db, tx, settlement.DeviceId, settlement.Generation))
                MergeContributionFromSettlement(db, tx, settlement, listenMs, localDate, firstPlayedAtUtcMs, lastPlayedAtUtcMs);
            return true;
        }

        internal static void MergeContributionFromSettlement(IDbConnection db, IDbTransaction tx, PlaybackSyncSettlement settlement, long listenMs, string localDate, long? firstPlayedAtUtcMs, long? lastPlayedAtUtcMs)
        {
            db.Execute(@"INSERT OR IGNORE INTO PlaybackSyncContributions VALUES (@SongId,@DeviceId,@Generation,0,NULL,NULL,@AppliedAtUtcMs)", settlement, tx);
            db.Execute(@"UPDATE PlaybackSyncContributions SET UndatedListenMs=CASE WHEN @Date IS NULL THEN CASE WHEN UndatedListenMs > 9223372036854775807 - @Listen THEN 9223372036854775807 ELSE UndatedListenMs + @Listen END ELSE UndatedListenMs END, FirstPlayedAtUtcMs=CASE WHEN @First IS NULL OR @First=0 THEN FirstPlayedAtUtcMs WHEN FirstPlayedAtUtcMs IS NULL OR FirstPlayedAtUtcMs=0 OR @First<FirstPlayedAtUtcMs THEN @First ELSE FirstPlayedAtUtcMs END, LastPlayedAtUtcMs=CASE WHEN @Last IS NULL OR @Last=0 THEN LastPlayedAtUtcMs WHEN LastPlayedAtUtcMs IS NULL OR @Last>LastPlayedAtUtcMs THEN @Last ELSE LastPlayedAtUtcMs END, UpdatedAtUtcMs=CASE WHEN UpdatedAtUtcMs>@Applied THEN UpdatedAtUtcMs ELSE @Applied END WHERE SongId=@SongId AND DeviceId=@DeviceId AND Generation=@Generation",
                new
                {
                    settlement.SongId,
                    settlement.DeviceId,
                    settlement.Generation,
                    Date = localDate,
                    Listen = listenMs,
                    First = firstPlayedAtUtcMs,
                    Last = lastPlayedAtUtcMs,
                    Applied = settlement.AppliedAtUtcMs
                },
                tx);

            if (localDate != null)
            {
                db.Execute(@"INSERT INTO PlaybackSyncDailyBuckets VALUES (@SongId,@DeviceId,@Generation,@Date,@Listen) ON CONFLICT(SongId,DeviceId,Generation,LocalDate) DO UPDATE SET ListenMs=CASE WHEN ListenMs > 9223372036854775807 - @Listen THEN 9223372036854775807 ELSE ListenMs + @Listen END",
                    new
                    {
                        settlement.SongId,
                        settlement.DeviceId,
                        settlement.Generation,
                        Date = localDate,
                        Listen = listenMs
                    },
                    tx);
            }
        }

        internal static int RelinkSongs(IDbConnection db, IDbTransaction tx)
        {
            var songs = db.Query<PlaybackSyncSong>("SELECT * FROM PlaybackSyncSongs WHERE LocalTrackId IS NULL ORDER BY SongId", transaction: tx).ToList();
            var tracks = LoadTracks(db, tx);
            var linked = 0;

            foreach (var song in songs)
            {
                var sameName = tracks.Where(track => track.NormalizedFileName == song.NormalizedFileName).ToList();
                var candidates = sameName.Where(track => IsExact(song, track)).ToList();
                if (TryBindUnique(db, tx, song, candidates, ref linked)) continue;
                if (candidates.Count > 1) continue;

                candidates = sameName.Where(track => song.TotalSamples.GetValueOrDefault() > 0 &&
                    track.TotalSamples.GetValueOrDefault() > 0 &&
                    DifferenceAtMost(song.TotalSamples.Value, track.TotalSamples.Value, 10000)).ToList();
                if (TryBindUnique(db, tx, song, candidates, ref linked)) continue;
                if (candidates.Count > 1) continue;

                candidates = sameName.Where(track => DifferenceAtMost(song.DurationMs, track.DurationMs, 200)).ToList();
                if (TryBindUnique(db, tx, song, candidates, ref linked)) continue;
                if (candidates.Count > 1) continue;

                candidates = sameName.Count == 1 ? sameName : new List<RelinkTrack>();
                TryBindUnique(db, tx, song, candidates, ref linked);
            }
            return linked;
        }

        private static List<RelinkTrack> LoadTracks(IDbConnection db, IDbTransaction tx)
        {
            return db.Query<RelinkTrack>("SELECT Id,FileName,DurationMs,TotalSamples FROM Tracks ORDER BY Id", transaction: tx).Select(track =>
            {
                track.NormalizedFileName = SyncSnapshotSerializer.NormalizePlaybackSongFileName(track.FileName);
                return track;
            }).ToList();
        }

        private static bool IsExact(PlaybackSyncSong song, RelinkTrack track)
        {
            return song.NormalizedFileName == track.NormalizedFileName && song.DurationMs == track.DurationMs;
        }

        private static bool TryBindUnique(IDbConnection db, IDbTransaction tx, PlaybackSyncSong song, IReadOnlyCollection<RelinkTrack> candidates, ref int linked)
        {
            if (candidates.Count != 1) return false;

            linked += db.Execute(
                "UPDATE PlaybackSyncSongs SET LocalTrackId=@TrackId WHERE SongId=@SongId AND LocalTrackId IS NULL",
                new { TrackId = candidates.Single().Id, SongId = song.SongId },
                tx);
            return true;
        }

        private static bool DifferenceAtMost(long left, long right, long tolerance)
        {
            var difference = left >= right ? left - right : right - left;
            return difference <= tolerance;
        }

        private sealed class RelinkTrack
        {
            public int Id { get; set; }
            public string FileName { get; set; }
            public long DurationMs { get; set; }
            public long? TotalSamples { get; set; }
            public string NormalizedFileName { get; set; }
        }
    }
}
