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
                        p.Id, p.Name, p.FolderPath, p.IsFolderLinked, p.CreatedAt, p.SortOrder,
                        (SELECT COUNT(1) FROM PlaylistItems pi WHERE pi.PlaylistId = p.Id) AS SongCount
                    FROM Playlists p
                    ORDER BY p.SortOrder ASC, p.CreatedAt DESC";
                var result = await db.QueryAsync<Playlist>(sql);
                return result.ToList();
            }
        }

        public int Add(string name, string folderPath = null, bool isLinked = false)
        {
            using (var db = GetConnection())
            {
                return db.ExecuteScalar<int>(@"
                    INSERT INTO Playlists (Name, FolderPath, IsFolderLinked) 
                    VALUES (@Name, @Path, @Linked);
                    SELECT last_insert_rowid();", 
                    new { Name = name, Path = folderPath, Linked = isLinked ? 1 : 0 });
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

        public void BulkSaveTracksToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId)
        {
            // 注意：此方法逻辑较为复杂，建议通过 DatabaseHelper 或调用更通用的 BulkInsert 逻辑
            // 为了维持 3NF 一致性，这里应调用统一的保存逻辑
            using (var db = GetConnection())
            using (var trans = db.BeginTransaction())
            {
                foreach (var track in tracks)
                {
                    // 1. 查找或插入 Artist/Album (这里逻辑应与 DatabaseHelper 一致)
                    // ... 简略起见，这里复用逻辑的核心是先保证 Track 存在 ...
                    // 鉴于目前架构，建议直接在 UI 层调用 TrackRepository 进行保存后再加入歌单
                }
                trans.Commit();
            }
        }

        public void AddFolder(int playlistId, string folderPath)
        {
            using (var db = GetConnection())
            {
                db.Execute(@"INSERT OR IGNORE INTO PlaylistFolders (PlaylistId, FolderPath) VALUES (@Pid, @Path)", 
                    new { Pid = playlistId, Path = folderPath });
            }
        }

        public void RemoveFolder(int playlistId, string folderPath)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM PlaylistFolders WHERE PlaylistId = @Pid AND FolderPath = @Path", 
                    new { Pid = playlistId, Path = folderPath });
            }
        }

        public IEnumerable<string> GetFolders(int playlistId)
        {
            using (var db = GetConnection())
            {
                return db.Query<string>(
                    "SELECT FolderPath FROM PlaylistFolders WHERE PlaylistId = @Pid ORDER BY AddedAt ASC", 
                    new { Pid = playlistId }).ToList();
            }
        }
    }
}
