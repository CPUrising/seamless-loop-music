using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public interface ITrackRepository
    {
        IEnumerable<MusicTrack> GetAll();
        Task<List<MusicTrack>> GetAllAsync();
        MusicTrack GetByFingerprint(string fileName, long totalSamples);
        void Save(MusicTrack track);
        void BulkInsert(IEnumerable<MusicTrack> tracks);
        void UpdateLoopPoints(int trackId, long start, long end);
    }
}

