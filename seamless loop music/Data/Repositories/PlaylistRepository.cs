using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data;
using System.Linq;
using Dapper;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public class PlaylistRepository : BaseRepository, IPlaylistRepository
    {
        public PlaylistRepository() : base(null) { }
        public PlaylistRepository(string customDbPath) : base(customDbPath) { }

        // ── 复用统一的 3NF 查询逻辑 ──────────────────────────────────────────
        private const string FullTrackSelect = @"
            SELECT 
                t.Id, t.FileName, t.FilePath, t.DisplayName, t.TotalSamples, t.LastModified, t.CoverPath,
                al.Name AS Album, ar.Name AS Artist, ar.Name AS AlbumArtist,
                COALESCE(lp.LoopStart, 0) AS LoopStart, COALESCE(lp.LoopEnd, 0) AS LoopEnd,
                lp.LoopCandidatesJson,
                COALESCE(ur.IsLoved, 0) AS IsLoved, COALESCE(ur.Rating, 0) AS Rating
            FROM Tracks t
            LEFT JOIN Albums al ON t.AlbumId = al.Id
            LEFT JOIN Artists ar ON al.ArtistId = ar.Id
            LEFT JOIN LoopPoints lp ON t.Id = lp.TrackId
            LEFT JOIN UserRatings ur ON t.Id = ur.TrackId ";

        public async Task<List<Playlist>> GetAllAsync()
        {
            using (var db = GetConnection())
            {
                string sql = @"
                    SELECT 
                        p.Id, p.Name, p.CreatedAt, p.SortOrder,
                        (SELECT COUNT(1) FROM PlaylistItems pi WHERE pi.PlaylistId = p.Id) AS SongCount
                    FROM Playlists p
                    ORDER BY p.SortOrder ASC, p.CreatedAt DESC";
                var result = await db.QueryAsync<Playlist>(sql);
                return result.ToList();
            }
        }

        public int Add(string name)
        {
            using (var db = GetConnection())
            {
                return db.ExecuteScalar<int>(@"
                    INSERT INTO Playlists (Name) 
                    VALUES (@Name);
                    SELECT last_insert_rowid();", 
                    new { Name = name });
            }
        }

        public void Delete(int id)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM Playlists WHERE Id = @Id", new { Id = id });
            }
        }

        public void Rename(int id, string newName)
        {
            using (var db = GetConnection())
            {
                db.Execute("UPDATE Playlists SET Name = @Name WHERE Id = @Id", new { Name = newName, Id = id });
            }
        }

        public void UpdateSortOrder(IEnumerable<int> ids)
        {
            var idList = ids.ToList();
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                for (int i = 0; i < idList.Count; i++)
                {
                    db.Execute("UPDATE Playlists SET SortOrder = @Order WHERE Id = @Id", 
                        new { Order = i, Id = idList[i] }, transaction: trans);
                }
                trans.Commit();
            }
        }

        public async Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId)
        {
            using (var db = GetConnection())
            {
                // 使用 JOIN 关联 PlaylistItems，并应用统一的 3NF 选择器
                // 显式选择 t.Id 确保 Dapper 映射到 MusicTrack.Id
                string sql = FullTrackSelect + @"
                    JOIN PlaylistItems pi ON t.Id = pi.SongId
                    WHERE pi.PlaylistId = @Pid
                    ORDER BY pi.SortOrder ASC";
                var result = await db.QueryAsync<MusicTrack>(sql, new { Pid = playlistId });
                return result.ToList();
            }
        }

        public void AddTrack(int playlistId, int trackId)
        {
            using (var db = GetConnection())
            {
                if (IsTrackInPlaylist(playlistId, trackId)) return;

                int maxOrder = db.ExecuteScalar<int>(
                    "SELECT IFNULL(MAX(SortOrder), 0) FROM PlaylistItems WHERE PlaylistId = @Pid", 
                    new { Pid = playlistId });
                
                db.Execute(
                    "INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@Pid, @Sid, @Order)", 
                    new { Pid = playlistId, Sid = trackId, Order = maxOrder + 1 });
            }
        }

        public void RemoveTrack(int playlistId, int trackId)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM PlaylistItems WHERE PlaylistId = @Pid AND SongId = @Sid", 
                    new { Pid = playlistId, Sid = trackId });
            }
        }

        public void UpdateTracksSortOrder(int playlistId, IEnumerable<int> trackIds)
        {
            var idList = trackIds.ToList();
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                for (int i = 0; i < idList.Count; i++)
                {
                    db.Execute("UPDATE PlaylistItems SET SortOrder = @Order WHERE PlaylistId = @Pid AND SongId = @Sid", 
                        new { Order = i, Pid = playlistId, Sid = idList[i] }, transaction: trans);
                }
                trans.Commit();
            }
        }

        public bool IsTrackInPlaylist(int playlistId, int trackId)
        {
            using (var db = GetConnection())
            {
                return db.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM PlaylistItems WHERE PlaylistId = @Pid AND SongId = @Sid", 
                    new { Pid = playlistId, Sid = trackId }) > 0;
            }
        }


    }
}
