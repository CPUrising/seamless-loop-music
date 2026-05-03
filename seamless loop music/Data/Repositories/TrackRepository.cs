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
                al.CoverPath AS AlbumCoverPath,
                ar.CoverPath AS ArtistCoverPath,
                COALESCE(lp.LoopStart, 0)  AS LoopStart,
                COALESCE(lp.LoopEnd,   0)  AS LoopEnd,
                lp.LoopCandidatesJson,
                COALESCE(ur.Rating,    0)  AS Rating
            FROM Tracks t
            LEFT JOIN Albums    al ON al.Id = t.AlbumId
            LEFT JOIN Artists   ar ON ar.Id = t.ArtistId
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
                    // 1. Upsert Artist / Album → 获取 IDs
                    var (albumId, artistId) = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);

                    if (track.Id > 0)
                    {
                        // 更新 Tracks
                        db.Execute(@"
                            UPDATE Tracks SET
                                DisplayName  = @DisplayName,
                                FilePath     = @FilePath,
                                LastModified = @LastModified,
                                CoverPath    = @CoverPath,
                                AlbumId      = @AlbumId,
                                ArtistId     = @ArtistId
                            WHERE Id = @Id;",
                            new { track.DisplayName, track.FilePath, track.LastModified, track.CoverPath, AlbumId = albumId, ArtistId = artistId, track.Id },
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
                                INSERT INTO Tracks (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId, ArtistId)
                                VALUES (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId, @ArtistId);
                                SELECT last_insert_rowid();",
                                new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId, ArtistId = artistId },
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
                        var (albumId, artistId) = UpsertArtistAlbum(db, track.Artist, track.AlbumArtist, track.Album, track.CoverPath, trans);

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
                                    ArtistId     = @ArtistId,
                                    TotalSamples = @TotalSamples  -- 更新为最新的物理采样数
                                WHERE Id = @Id;",
                                new { track.FilePath, track.DisplayName, track.CoverPath, track.LastModified, AlbumId = albumId, ArtistId = artistId, Id = trackId, track.TotalSamples },
                                transaction: trans);
                        }
                        else
                        {
                            trackId = db.ExecuteScalar<long>(@"
                                INSERT INTO Tracks (FileName, FilePath, DisplayName, TotalSamples, LastModified, CoverPath, AlbumId, ArtistId)
                                VALUES (@FileName, @FilePath, @DisplayName, @TotalSamples, @LastModified, @CoverPath, @AlbumId, @ArtistId);
                                SELECT last_insert_rowid();",
                                new { track.FileName, track.FilePath, track.DisplayName, track.TotalSamples, track.LastModified, track.CoverPath, AlbumId = albumId, ArtistId = artistId },
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

                // 扫描完成后，自动运行一次修复逻辑，确保分类封面被正确补全
                RepairMissingCategoryCovers();
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
        
        public async Task<string> GetAlbumCoverPathAsync(string albumName)
        {
            using (var db = GetConnection())
            {
                return await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT CoverPath FROM Albums WHERE Name = @Name", new { Name = albumName });
            }
        }

        public async Task<string> GetArtistCoverPathAsync(string artistName)
        {
            using (var db = GetConnection())
            {
                return await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT CoverPath FROM Artists WHERE Name = @Name", new { Name = artistName });
            }
        }

        // ── 辅助：Upsert 艺术家 / 专辑，返回 AlbumId ─────────────────────────
        private static (int? albumId, int? artistId) UpsertArtistAlbum(IDbConnection db, string artist, string albumArtist,
                                               string album, string coverPath, IDbTransaction trans)
        {
            // 严格统一：使用 Trim() 并处理空白字符，确保唯一键匹配
            string artistName = !string.IsNullOrWhiteSpace(artist) ? artist.Trim() : (!string.IsNullOrWhiteSpace(albumArtist) ? albumArtist.Trim() : "Unknown Artist");
            string albumName = !string.IsNullOrWhiteSpace(album) ? album.Trim() : "Unknown Album";

            // 1. 处理艺术家 (独立)
            db.Execute("INSERT OR IGNORE INTO Artists (Name, CoverPath) VALUES (@Name, NULL);",
                new { Name = artistName }, transaction: trans);
            int artistId = db.ExecuteScalar<int>(
                "SELECT Id FROM Artists WHERE Name = @Name;",
                new { Name = artistName }, transaction: trans);

            if (!string.IsNullOrEmpty(coverPath))
            {
                db.Execute(@"UPDATE Artists 
                            SET CoverPath = @Cover 
                            WHERE Name = @Name AND (CoverPath IS NULL OR CoverPath = '')", 
                            new { Name = artistName, Cover = coverPath }, transaction: trans);
            }

            // 2. 处理专辑 (仅按名称指纹去重)
            db.Execute(@"
                INSERT INTO Albums (Name, CoverPath)
                VALUES (@Name, @CoverPath)
                ON CONFLICT(Name) DO UPDATE SET
                    CoverPath = CASE WHEN Albums.CoverPath IS NULL OR Albums.CoverPath = '' THEN excluded.CoverPath ELSE Albums.CoverPath END;",
                new { Name = albumName, CoverPath = coverPath }, transaction: trans);

            int albumId = db.ExecuteScalar<int>(
                "SELECT Id FROM Albums WHERE Name = @Name;",
                new { Name = albumName }, transaction: trans);

            return (albumId, artistId);
        }

        public void RepairMissingCategoryCovers()
        {
            using (var db = GetConnection())
            {
                if (db.State != ConnectionState.Open) db.Open();

                // 1. 获取所有专辑，准备进行物理有效性校验
                var albums = db.Query("SELECT Id, Name, CoverPath FROM Albums").ToList();
                foreach (var album in albums)
                {
                    string name = (string)album.Name;
                    string path = (string)album.CoverPath;

                    // 规则 A：如果是 Unknown 专辑，严禁任何形式的封面扩散
                    if (name == "Unknown Album")
                    {
                        if (!string.IsNullOrEmpty(path))
                            db.Execute("UPDATE Albums SET CoverPath = NULL WHERE Id = @Id", new { Id = album.Id });
                        continue;
                    }

                    // 规则 B：校验物理文件是否存在，不存在则清理
                    if (!string.IsNullOrEmpty(path) && !File.Exists(path))
                    {
                        db.Execute("UPDATE Albums SET CoverPath = NULL WHERE Id = @Id", new { Id = album.Id });
                        path = null;
                    }

                    // 规则 C：向上扩散 (Track -> Album)
                    if (string.IsNullOrEmpty(path))
                    {
                        // 寻找该专辑下第一个具备有效物理封面的曲目
                        var tracks = db.Query("SELECT CoverPath FROM Tracks WHERE AlbumId = @Id AND CoverPath IS NOT NULL AND CoverPath != ''", new { Id = album.Id });
                        foreach (var t in tracks)
                        {
                            string tPath = (string)t.CoverPath;
                            if (File.Exists(tPath))
                            {
                                db.Execute("UPDATE Albums SET CoverPath = @Path WHERE Id = @Id", new { Path = tPath, Id = album.Id });
                                break;
                            }
                        }
                    }
                }

                // 2. 艺术家层面同步 (Album -> Artist)
                var artists = db.Query("SELECT Id, Name, CoverPath FROM Artists").ToList();
                foreach (var artist in artists)
                {
                    string name = (string)artist.Name;
                    string path = (string)artist.CoverPath;

                    if (name == "Unknown Artist")
                    {
                        if (!string.IsNullOrEmpty(path))
                            db.Execute("UPDATE Artists SET CoverPath = NULL WHERE Id = @Id", new { Id = artist.Id });
                        continue;
                    }

                    if (!string.IsNullOrEmpty(path) && !File.Exists(path))
                    {
                        db.Execute("UPDATE Artists SET CoverPath = NULL WHERE Id = @Id", new { Id = artist.Id });
                        path = null;
                    }

                    if (string.IsNullOrEmpty(path))
                    {
                        // 寻找该艺术家唱过的歌曲所属的、具备有效物理封面的专辑
                        var artistAlbums = db.Query(@"
                            SELECT DISTINCT al.CoverPath 
                            FROM Tracks t 
                            JOIN Albums al ON al.Id = t.AlbumId 
                            WHERE t.ArtistId = @Id 
                              AND al.CoverPath IS NOT NULL 
                              AND al.CoverPath != ''", new { Id = artist.Id });
                        
                        foreach (var al in artistAlbums)
                        {
                            string alPath = (string)al.CoverPath;
                            if (File.Exists(alPath))
                            {
                                db.Execute("UPDATE Artists SET CoverPath = @Path WHERE Id = @Id", new { Path = alPath, Id = artist.Id });
                                break;
                            }
                        }
                    }
                }

                // 3. 向下补全 (Album -> Track)
                // 只有当曲目本身没封面（或坏了），且它不是 Unknown 专辑时，才从专辑拉取封面
                db.Execute(@"
                    UPDATE Tracks 
                    SET CoverPath = (SELECT al.CoverPath FROM Albums al WHERE al.Id = Tracks.AlbumId)
                    WHERE (CoverPath IS NULL OR CoverPath = '')
                      AND AlbumId IN (SELECT Id FROM Albums WHERE Name != 'Unknown Album' AND CoverPath IS NOT NULL AND CoverPath != '')");
                
                // 物理校验：如果 Track 现有的 CoverPath 坏了，也尝试补全
                var brokenTracks = db.Query("SELECT t.Id, t.CoverPath, al.CoverPath as AlbumPath FROM Tracks t JOIN Albums al ON al.Id = t.AlbumId WHERE t.CoverPath IS NOT NULL AND t.CoverPath != '' AND al.Name != 'Unknown Album' AND al.CoverPath IS NOT NULL AND al.CoverPath != ''").ToList();
                foreach (var t in brokenTracks)
                {
                    if (!File.Exists((string)t.CoverPath))
                    {
                        db.Execute("UPDATE Tracks SET CoverPath = @Path WHERE Id = @Id", new { Path = (string)t.AlbumPath, Id = (long)t.Id });
                    }
                }
            }
        }
    }
}
