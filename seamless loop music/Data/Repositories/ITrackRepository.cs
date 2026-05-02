using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public interface ITrackRepository
    {
        IEnumerable<MusicTrack> GetAll();
        Task<List<MusicTrack>> GetAllAsync();
        Task<MusicTrack> GetByIdAsync(int id);
        MusicTrack GetByFingerprint(string fileName, long totalSamples);
        void Save(MusicTrack track);
        void BulkInsert(IEnumerable<MusicTrack> tracks);
        void UpdateLoopPoints(int trackId, long start, long end);
        Task UpdateMetadataAsync(int id, int rating);
        Task UpdateMetadataAsync(MusicTrack track);
        Task DeleteAsync(int trackId);
        Task<List<MusicTrack>> GetByArtistAsync(string artistName);
        Task<List<MusicTrack>> GetByAlbumAsync(string albumName);
        Task<string> GetAlbumCoverPathAsync(string albumName);
        Task<string> GetArtistCoverPathAsync(string artistName);
    }
}
