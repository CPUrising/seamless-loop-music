using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public interface IPlaybackStatisticsRepository
    {
        Task RecordPlaybackSegmentAsync(string segmentId, int trackId, long startedAtUtcMs, long durationMs);
        Task<int> ClearAllAsync();
        Task<List<PlaybackStatisticItem>> GetTopTracksAsync(PlaybackStatisticsPeriod period, int limit = 5, DateTime? nowLocal = null);
    }
}
