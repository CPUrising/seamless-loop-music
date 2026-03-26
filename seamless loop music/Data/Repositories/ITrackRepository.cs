using System.Collections.Generic;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public interface ITrackRepository
    {
        IEnumerable<MusicTrack> GetAll();
        MusicTrack GetByFingerprint(string fileName, long totalSamples);
        void Save(MusicTrack track);
        void BulkInsert(IEnumerable<MusicTrack> tracks);
    }
}
