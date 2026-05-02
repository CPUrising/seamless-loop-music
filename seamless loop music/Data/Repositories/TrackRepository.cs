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
        public TrackRepository() : base(null) { }
        public TrackRepository(string customDbPath) : base(customDbPath) { }

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
                COALESCE(ur.Rating,    0)  AS Rating
            FROM Tracks t
            LEFT JOIN Albums    al ON al.Id = t.AlbumId
            LEFT JOIN Artists   ar ON ar.Id = al.ArtistId
            LEFT JOIN LoopPoints lp ON lp.TrackId = t.Id
            LEFT JOIN UserRatings ur ON ur.TrackId = t.Id ";

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

        public async Task<MusicTrack> GetByIdAsync(int id)
        {
            using (var db = GetConnection())
            {
                return await db.QueryFirstOrDefaultAsync<MusicTrack>(
                    FullTrackSelect + " WHERE t.Id = @Id", new { Id = id });
            }
        }

        public MusicTrack GetByFingerprint(string fileName, long totalSamples)
        {
            using (var db = GetConnection())
            {
                return db.QueryFirstOrDefault<MusicTrack>(
                    FullTrackSelect + @"
                    WHERE LOWER(TRIM(t.FileName)) = LOWER(TRIM(@FileName)) 
                      AND ABS(t.TotalSamples - @Total) < 10000",
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

                        // 更新 UserRatings (补全)
                        db.Execute(@"
                            INSERT INTO UserRatings (TrackId, Rating, LastModified)
                            VALUES (@TrackId, @Rating, @Now)
                            ON CONFLICT(TrackId) DO UPDATE SET
                                Rating       = excluded.Rating,
                                LastModified = excluded.LastModified;",
                            new { TrackId = track.Id, track.Rating, Now = DateTime.Now },
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
                            // 插入/更新 LoopPoints
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

                            // 插入/更新 UserRatings
                            db.Execute(@"
                                INSERT INTO UserRatings (TrackId, Rating, LastModified)
                                VALUES (@TrackId, @Rating, @Now)
                                ON CONFLICT(TrackId) DO UPDATE SET
                                    Rating       = excluded.Rating,
                                    LastModified = excluded.LastModified;",
                                new { TrackId = track.Id, track.Rating, Now = DateTime.Now },
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

                        // --- 核心优化：先尝试进行模糊查重，防止产生采样数微差的影子记录 ---
                        var existingId = db.ExecuteScalar<long?>(@"
                            SELECT Id FROM Tracks 
                            WHERE LOWER(TRIM(FileName)) = LOWER(TRIM(@FileName)) 
                              AND ABS(TotalSamples - @TotalSamples) < 10000",
                            new { track.FileName, track.TotalSamples }, transaction: trans);

                        long trackId;
                        if (existingId.HasValue)
                        {
                            trackId = existingId.Value;
                            db.Execute(@"
                                UPDATE Tracks SET
                                    FilePath     = @FilePath,
                                    DisplayName  = @DisplayName,
                                    CoverPath    = @CoverPath,
                                    LastModified = @LastModified,
                                    AlbumId      = @AlbumId,
                                    TotalSamples = @TotalSamples  -- 更新为最新的物理采样数
                                WHERE Id = @Id;",
                                new { track.FilePath, track.DisplayName, track.CoverPath, track.LastModified, AlbumId = albumId, Id = trackId, track.TotalSamples },
                                transaction: trans);
                        }
                        else
                        {
                            trackId = db.ExecuteScalar<long>(@"
                                INSERT INTO Tracks (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId)
                                VALUES (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId);
                                SELECT last_insert_rowid();",
                                new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId },
                                transaction: trans);
                        }

                        db.Execute(@"
                            INSERT INTO LoopPoints (TrackId, LoopStart, LoopEnd, LoopCandidatesJson, AnalysisLastModified)
                            VALUES (@TrackId, @LoopStart, @LoopEnd, @Json, @Now)
                            ON CONFLICT(TrackId) DO UPDATE SET
                                LoopStart            = excluded.LoopStart,
                                LoopEnd              = excluded.LoopEnd,
                                LoopCandidatesJson   = excluded.LoopCandidatesJson,
                                AnalysisLastModified = excluded.AnalysisLastModified;",
                            new { TrackId = trackId, track.LoopStart, track.LoopEnd, Json = track.LoopCandidatesJson, Now = track.LastModified },
                            transaction: trans);

                        db.Execute(@"
                            INSERT INTO UserRatings (TrackId, Rating, LastModified)
                            VALUES (@TrackId, @Rating, @Now)
                            ON CONFLICT(TrackId) DO UPDATE SET
                                Rating       = excluded.Rating,
                                LastModified = excluded.LastModified;",
                            new { TrackId = trackId, track.Rating, Now = track.LastModified },
                            transaction: trans);

                        track.Id = (int)trackId;
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

        public async Task UpdateMetadataAsync(int id, int rating)
        {
            using (var db = GetConnection())
            {
                await db.ExecuteAsync(@"
                    INSERT INTO UserRatings (TrackId, Rating, LastModified)
                    VALUES (@TrackId, @R, @Now)
                    ON CONFLICT(TrackId) DO UPDATE SET
                        Rating       = excluded.Rating,
                        LastModified = excluded.LastModified;",
                    new { TrackId = id, R = rating, Now = DateTime.Now });
            }
        }

        public async Task UpdateMetadataAsync(MusicTrack track)
        {
            using (var db = GetConnection())
            {
                await db.ExecuteAsync(@"UPDATE Tracks SET DisplayName = @DisplayName WHERE Id = @Id", 
                    new { track.DisplayName, track.Id });
                
                await UpdateMetadataAsync(track.Id, track.Rating);
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
            string artistName = !string.IsNullOrWhiteSpace(artist) ? artist : (!string.IsNullOrWhiteSpace(albumArtist) ? albumArtist : "Unknown Artist");
            string albumName = !string.IsNullOrWhiteSpace(album) ? album : "Unknown Album";

            db.Execute("INSERT OR IGNORE INTO Artists (Name) VALUES (@Name);",
                new { Name = artistName }, transaction: trans);
            int artistId = db.ExecuteScalar<int>(
                "SELECT Id FROM Artists WHERE Name = @Name;",
                new { Name = artistName }, transaction: trans);

            // Album: 升级为 ON CONFLICT 模式，如果发现新封面则自动补全
            db.Execute(@"
                INSERT INTO Albums (Name, ArtistId, CoverPath)
                VALUES (@Name, @ArtistId, @CoverPath)
                ON CONFLICT(Name, ArtistId) DO UPDATE SET
                    CoverPath = COALESCE(Albums.CoverPath, excluded.CoverPath)
                WHERE Albums.CoverPath IS NULL OR Albums.CoverPath = '';",
                new { Name = albumName, ArtistId = artistId, CoverPath = coverPath }, transaction: trans);

            return db.ExecuteScalar<int?>(
                "SELECT Id FROM Albums WHERE Name = @Name AND (ArtistId = @ArtistId OR (ArtistId IS NULL AND @ArtistId IS NULL));",
                new { Name = albumName, ArtistId = artistId }, transaction: trans);
        }
    }
}
