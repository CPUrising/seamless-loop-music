using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public class TrackRepository : BaseRepository, ITrackRepository
    {
        // ── 完整 JOIN 查询语句（供所有 SELECT 复用）────────────────────────────
        private const string FullTrackSelect = @"
            SELECT
                t.Id,
                t.FileName,
                t.FilePath,
                t.DisplayName,
                t.TotalSamples,
                t.LastModified,
                t.CoverPath,
                al.Name   AS Album,
                ar.Name   AS Artist,
                ar.Name   AS AlbumArtist,
                COALESCE(lp.LoopStart, 0)  AS LoopStart,
                COALESCE(lp.LoopEnd,   0)  AS LoopEnd,
                lp.LoopCandidatesJson,
                COALESCE(ur.IsLoved,   0)  AS IsLoved,
                COALESCE(ur.Rating,    0)  AS Rating
            FROM Tracks t
            LEFT JOIN Albums    al ON al.Id = t.AlbumId
            LEFT JOIN Artists   ar ON ar.Id = al.ArtistId
            LEFT JOIN LoopPoints lp ON lp.TrackId = t.Id
            LEFT JOIN UserRatings ur ON ur.TrackId = t.Id";

        // ────────────────────────────────────────────────────────────────────
        public IEnumerable<MusicTrack> GetAll()
        {
            using (var db = GetConnection())
            {
                return db.Query<MusicTrack>(FullTrackSelect);
            }
        }

        public async Task<List<MusicTrack>> GetAllAsync()
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>(FullTrackSelect);
                return result.ToList();
            }
        }

        public MusicTrack GetByFingerprint(string fileName, long totalSamples)
        {
            using (var db = GetConnection())
            {
                return db.QueryFirstOrDefault<MusicTrack>(
                    FullTrackSelect + @"
                    WHERE t.FileName = @FileName AND t.TotalSamples = @Total",
                    new { FileName = fileName, Total = totalSamples });
            }
        }

        public void Save(MusicTrack track)
        {
            if (string.IsNullOrEmpty(track.FileName) && !string.IsNullOrEmpty(track.FilePath))
                track.FileName = Path.GetFileName(track.FilePath);

            track.LastModified = DateTime.Now;

            using (var db = GetConnection())
            {
                using (var trans = db.BeginTransaction())
                {
                    // 1. Upsert Artist / Album → 获取 AlbumId
                    int? albumId = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);

                    if (track.Id > 0)
                    {
                        // 更新 Tracks
                        db.Execute(@"
                            UPDATE Tracks SET
                                DisplayName  = @DisplayName,
                                FilePath     = @FilePath,
                                LastModified = @LastModified,
                                CoverPath    = @CoverPath,
                                AlbumId      = @AlbumId
                            WHERE Id = @Id;",
                            new { track.DisplayName, track.FilePath, track.LastModified, track.CoverPath, AlbumId = albumId, track.Id },
                            transaction: trans);

                        // 更新 LoopPoints
                        db.Execute(@"
                            INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                            VALUES (@TrackId, @LoopStart, @LoopEnd, @Json, @Now)
                            ON CONFLICT(TrackId) DO UPDATE SET
                                LoopStart            = excluded.LoopStart,
                                LoopEnd              = excluded.LoopEnd,
                                LoopCandidatesJson   = excluded.LoopCandidatesJson,
                                AnalysisLastModified = excluded.AnalysisLastModified;",
                            new { TrackId = track.Id, track.LoopStart, track.LoopEnd, Json = track.LoopCandidatesJson, Now = DateTime.Now },
                            transaction: trans);
                    }
                    else
                    {
                        // 插入 Tracks
                        long newId;
                        try
                        {
                            newId = db.ExecuteScalar<long>(@"
                                INSERT INTO Tracks (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId)
                                VALUES (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId);
                                SELECT last_insert_rowid();",
                                new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId },
                                transaction: trans);
                            track.Id = (int)newId;
                        }
                        catch
                        {
                            var existing = db.QueryFirstOrDefault<MusicTrack>(
                                "SELECT Id FROM Tracks WHERE FileName = @FileName AND TotalSamples = @Total",
                                new { track.FileName, Total = track.TotalSamples }, transaction: trans);
                            if (existing != null) { track.Id = existing.Id; }
                        }

                        if (track.Id > 0)
                        {
                            // 插入 LoopPoints
                            db.Execute(@"
                                INSERT OR IGNORE INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                                VALUES (@TrackId, @LoopStart, @LoopEnd, @Json, @Now);",
                                new { TrackId = track.Id, track.LoopStart, track.LoopEnd, Json = track.LoopCandidatesJson, Now = DateTime.Now },
                                transaction: trans);

                            // 插入 UserRatings
                            db.Execute(@"
                                INSERT OR IGNORE INTO UserRatings (TrackId, Rating, IsLoved, LastModified)
                                VALUES (@TrackId, @Rating, @IsLoved, @Now);",
                                new { TrackId = track.Id, track.Rating, IsLoved = track.IsLoved ? 1 : 0, Now = DateTime.Now },
                                transaction: trans);
                        }
                    }

                    trans.Commit();
                }
            }
        }

        public void BulkInsert(IEnumerable<MusicTrack> tracks)
        {
            using (var db = GetConnection())
            {
                if (db.State != ConnectionState.Open) db.Open();
                using (var trans = db.BeginTransaction())
                {
                    foreach (var track in tracks)
                    {
                        int? albumId = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);

                        long trackId = db.ExecuteScalar<long>(@"
                            INSERT INTO Tracks (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId)
                            VALUES (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId)
                            ON CONFLICT(FileName, TotalSamples) DO UPDATE SET
                                FilePath     = excluded.FilePath,
                                DisplayName  = excluded.DisplayName,
                                CoverPath    = excluded.CoverPath,
                                LastModified = excluded.LastModified,
                                AlbumId      = excluded.AlbumId;
                            SELECT Id FROM Tracks WHERE FileName = @FileName AND TotalSamples = @TotalSamples;",
                            new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId },
                            transaction: trans);

                        db.Execute(@"
                            INSERT OR IGNORE INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                            VALUES (@TrackId, @LoopStart, @LoopEnd, @Json, @Now);",
                            new { TrackId = trackId, track.LoopStart, track.LoopEnd, Json = track.LoopCandidatesJson, Now = track.LastModified },
                            transaction: trans);

                        db.Execute(@"
                            INSERT OR IGNORE INTO UserRatings (TrackId, Rating, IsLoved, LastModified)
                            VALUES (@TrackId, @Rating, @IsLoved, @Now);",
                            new { TrackId = trackId, track.Rating, IsLoved = track.IsLoved ? 1 : 0, Now = track.LastModified },
                            transaction: trans);
                    }
                    trans.Commit();
                }
            }
        }

        public void UpdateLoopPoints(int trackId, long start, long end)
        {
            using (var db = GetConnection())
            {
                db.Execute(@"
                    INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, AnalysisLastModified)
                    VALUES (@TrackId, @S, @E, @Now)
                    ON CONFLICT(TrackId) DO UPDATE SET
                        LoopStart            = excluded.LoopStart,
                        LoopEnd              = excluded.LoopEnd,
                        AnalysisLastModified = excluded.AnalysisLastModified;",
                    new { TrackId = trackId, S = start, E = end, Now = DateTime.Now });
            }
        }

        public async Task UpdateMetadataAsync(int id, bool isLoved, int rating)
        {
            using (var db = GetConnection())
            {
                await db.ExecuteAsync(@"
                    INSERT INTO UserRatings (TrackId, IsLoved, Rating, LastModified)
                    VALUES (@TrackId, @L, @R, @Now)
                    ON CONFLICT(TrackId) DO UPDATE SET
                        IsLoved      = excluded.IsLoved,
                        Rating       = excluded.Rating,
                        LastModified = excluded.LastModified;",
                    new { TrackId = id, L = isLoved ? 1 : 0, R = rating, Now = DateTime.Now });
            }
        }

        public async Task DeleteAsync(int trackId)
        {
            using (var db = GetConnection())
            {
                // LoopPoints 和 UserRatings 因为有 ON DELETE CASCADE 会自动删除
                await db.ExecuteAsync("DELETE FROM Tracks WHERE Id=@Id", new { Id = trackId });
            }
        }

        public async Task<List<MusicTrack>> GetLovedTracksAsync()
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>(
                    FullTrackSelect + " WHERE ur.IsLoved = 1");
                return result.ToList();
            }
        }

        public async Task<List<MusicTrack>> GetByArtistAsync(string artistName)
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>(
                    FullTrackSelect + " WHERE ar.Name = @A", new { A = artistName });
                return result.ToList();
            }
        }

        public async Task<List<MusicTrack>> GetByAlbumAsync(string albumName)
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>(
                    FullTrackSelect + " WHERE al.Name = @A", new { A = albumName });
                return result.ToList();
            }
        }

        // ── 辅助：Upsert 艺术家 / 专辑，返回 AlbumId ─────────────────────────
        private static int? UpsertArtistAlbum(IDbConnection db, string artist, string albumArtist,
                                               string album, string coverPath, IDbTransaction trans)
        {
            if (string.IsNullOrWhiteSpace(album)) return null;

            // Artist（优先使用 AlbumArtist，其次 Artist）
            string artistName = !string.IsNullOrWhiteSpace(albumArtist) ? albumArtist : artist;
            int? artistId = null;
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                db.Execute("INSERT OR IGNORE INTO Artists (Name) VALUES (@Name);",
                    new { Name = artistName }, transaction: trans);
                artistId = db.ExecuteScalar<int>(
                    "SELECT Id FROM Artists WHERE Name = @Name;",
                    new { Name = artistName }, transaction: trans);
            }

            // Album
            db.Execute(@"
                INSERT OR IGNORE INTO Albums (Name, ArtistId, CoverPath)
                VALUES (@Name, @ArtistId, @CoverPath);",
                new { Name = album, ArtistId = artistId, CoverPath = coverPath }, transaction: trans);

            return db.ExecuteScalar<int?>(
                "SELECT Id FROM Albums WHERE Name = @Name AND (ArtistId = @ArtistId OR (ArtistId IS NULL AND @ArtistId IS NULL));",
                new { Name = album, ArtistId = artistId }, transaction: trans);
        }
    }
}
