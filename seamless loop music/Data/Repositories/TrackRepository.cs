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
        public IEnumerable<MusicTrack> GetAll()
        {
            using (var db = GetConnection())
            {
                return db.Query<MusicTrack>("SELECT * FROM LoopPoints");
            }
        }

        public async Task<List<MusicTrack>> GetAllAsync()
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>("SELECT * FROM LoopPoints");
                return result.ToList();
            }
        }

        public MusicTrack GetByFingerprint(string fileName, long totalSamples)
        {
            using (var db = GetConnection())
            {
                return db.QueryFirstOrDefault<MusicTrack>(
                    @"SELECT Id, FileName, FilePath, TotalSamples, DisplayName, LoopStart, LoopEnd, 
                             Artist, Album, AlbumArtist, LastModified, LoopCandidatesJson 
                      FROM LoopPoints 
                      WHERE FileName = @FileName AND TotalSamples = @Total", 
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
                if (track.Id > 0)
                {
                    string sql = @"
                        UPDATE LoopPoints 
                        SET DisplayName = @DisplayName, 
                            FilePath = @FilePath,
                            LoopStart = @LoopStart, 
                            LoopEnd = @LoopEnd, 
                            Artist = @Artist,
                            Album = @Album,
                            AlbumArtist = @AlbumArtist,
                            LastModified = @LastModified,
                            LoopCandidatesJson = @LoopCandidatesJson
                        WHERE Id = @Id;";
                    db.Execute(sql, track);
                }
                else
                {
                    string sql = @"
                        INSERT INTO LoopPoints 
                        (FileName, FilePath, DisplayName, LoopStart, LoopEnd, TotalSamples, Artist, Album, AlbumArtist, LastModified, LoopCandidatesJson)
                        VALUES 
                        (@FileName, @FilePath, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @Artist, @Album, @AlbumArtist, @LastModified, @LoopCandidatesJson);
                        SELECT last_insert_rowid();";
                    
                    try 
                    {
                        long newId = db.ExecuteScalar<long>(sql, track);
                        track.Id = (int)newId;
                    } 
                    catch 
                    {
                        var existing = GetByFingerprint(track.FileName, track.TotalSamples);
                        if (existing != null) 
                        {
                            track.Id = existing.Id;
                            Save(track); 
                        }
                    }
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
                    string sql = @"
                        INSERT OR REPLACE INTO LoopPoints 
                        (FileName, FilePath, DisplayName, LoopStart, LoopEnd, TotalSamples, Artist, Album, AlbumArtist, LastModified, LoopCandidatesJson)
                        VALUES 
                        (@FileName, @FilePath, @DisplayName, @LoopStart, @LoopEnd, @TotalSamples, @Artist, @Album, @AlbumArtist, @LastModified, @LoopCandidatesJson);";
                    db.Execute(sql, tracks, transaction: trans);
                    trans.Commit();
                }
            }
        }

        public void UpdateLoopPoints(int trackId, long start, long end)
        {
            using (var db = GetConnection())
            {
                db.Execute("UPDATE LoopPoints SET LoopStart=@S, LoopEnd=@E WHERE Id=@Id", new { S = start, E = end, Id = trackId });
            }
        }

        public async Task UpdateMetadataAsync(int id, bool isLoved, int rating)
        {
            using (var db = GetConnection())
            {
                await db.ExecuteAsync("UPDATE LoopPoints SET IsLoved=@L, Rating=@R WHERE Id=@Id", 
                    new { L = isLoved ? 1 : 0, R = rating, Id = id });
            }
        }

        public async Task<List<MusicTrack>> GetLovedTracksAsync()
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>("SELECT * FROM LoopPoints WHERE IsLoved = 1");
                return result.ToList();
            }
        }

        public async Task<List<MusicTrack>> GetByArtistAsync(string artistName)
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>("SELECT * FROM LoopPoints WHERE Artist = @A", new { A = artistName });
                return result.ToList();
            }
        }

        public async Task<List<MusicTrack>> GetByAlbumAsync(string albumName)
        {
            using (var db = GetConnection())
            {
                var result = await db.QueryAsync<MusicTrack>("SELECT * FROM LoopPoints WHERE Album = @A", new { A = albumName });
                return result.ToList();
            }
        }
    }
}
