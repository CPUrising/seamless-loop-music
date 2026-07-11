using System.Collections.Generic;
using System.Linq;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public static class PlaybackStatisticsSettlementFilter
    {
        public static IReadOnlyList<PlaybackStatisticsSettlement> ExcludingGeneration(IEnumerable<PlaybackStatisticsSettlement> settlements, string deviceId, long generation)
        {
            return (settlements ?? Enumerable.Empty<PlaybackStatisticsSettlement>())
                .Where(x => x.DeviceId != deviceId || x.Generation != generation).ToList();
        }
    }
}
