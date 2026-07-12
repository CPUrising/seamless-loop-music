using System.Data;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    public interface IPlaybackStatisticsSyncSnapshotAdapter
    {
        SyncPlaybackStatistics Export(IDbConnection connection);
        void Apply(IDbConnection connection, IDbTransaction transaction, SyncPlaybackStatistics statistics);
        int RelinkExactAndUniqueFuzzy();
    }
}
