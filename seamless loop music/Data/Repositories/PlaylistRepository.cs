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
        public async Task<List<Playlist>> GetAllAsync()
        {
            using (var db = GetConnection())
            {
                string sql = @"
                    SELECT 
                        p.Id, 
                        p.Name, 
                        p.FolderPath, 
                        p.IsFolderLinked, 
                        p.CreatedAt,
                        p.SortOrder,
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
            {
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
        }

        public async Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId)
        {
            using (var db = GetConnection())
            {
                string sql = @"
                    SELECT s.Id, s.FileName, s.FilePath, s.TotalSamples, s.DisplayName, s.LoopStart, s.LoopEnd, 
                           s.Artist, s.Album, s.AlbumArtist, s.LastModified, s.LoopCandidatesJson, pi.SortOrder 
                    FROM LoopPoints s
                    JOIN PlaylistItems pi ON s.Id = pi.SongId
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
            {
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
            using (var db = GetConnection())
            {
                using (var trans = db.BeginTransaction())
                {
                    foreach (var track in tracks)
                    {
                        // 1. Save/Update Track (Simplified logic for brevity)
                        var existing = db.QueryFirstOrDefault<MusicTrack>(
                            "SELECT Id FROM LoopPoints WHERE FileName = @FileName AND TotalSamples = @TotalSamples", 
                            new { track.FileName, track.TotalSamples }, transaction: trans);

                        long trackId;
                        if (existing == null)
                        {
                            trackId = db.QuerySingle<long>(
                                @"INSERT INTO LoopPoints (FileName, FilePath, TotalSamples, DisplayName, LoopStart, LoopEnd, Artist, Album, AlbumArtist, LastModified, LoopCandidatesJson) 
                                  VALUES (@FileName, @FilePath, @TotalSamples, @DisplayName, @LoopStart, @LoopEnd, @Artist, @Album, @AlbumArtist, @LastModified, @LoopCandidatesJson);
                                  SELECT last_insert_rowid();", track, transaction: trans);
                        }
                        else
                        {
                            trackId = existing.Id;
                            db.Execute(@"
                                UPDATE LoopPoints 
                                SET FilePath = @FilePath, Artist = @Artist, Album = @Album, AlbumArtist = @AlbumArtist 
                                WHERE Id = @Id", 
                                new { track.FilePath, track.Artist, track.Album, track.AlbumArtist, Id = trackId }, transaction: trans);
                        }

                        // 2. Add to Playlist
                        if (!IsTrackInPlaylist(playlistId, (int)trackId))
                        {
                            db.Execute(
                                "INSERT INTO PlaylistItems (PlaylistId, SongId, SortOrder) VALUES (@PlaylistId, @MusicTrackId, 0)", 
                                new { PlaylistId = playlistId, MusicTrackId = trackId }, transaction: trans);
                        }
                    }
                    trans.Commit();
                }
            }
        }

        public void AddFolder(int playlistId, string folderPath)
        {
            using (var db = GetConnection())
            {
                db.Execute(@"
                    INSERT OR IGNORE INTO Playlists (PlaylistId, FolderPath) 
                    VALUES (@Pid, @Path)", 
                    new { Pid = playlistId, Path = folderPath });
            }
        }

        public void RemoveFolder(int playlistId, string folderPath)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM Playlists WHERE PlaylistId = @Pid AND FolderPath = @Path", 
                    new { Pid = playlistId, Path = folderPath });
            }
        }

        public IEnumerable<string> GetFolders(int playlistId)
        {
            using (var db = GetConnection())
            {
                return db.Query<string>(
                    "SELECT FolderPath FROM Playlists WHERE PlaylistId = @Pid ORDER BY AddedAt ASC", 
                    new { Pid = playlistId });
            }
        }
    }
}

