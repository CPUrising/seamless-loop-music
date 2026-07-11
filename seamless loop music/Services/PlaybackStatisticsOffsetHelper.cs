using System;

namespace seamless_loop_music.Services
{
    public static class PlaybackStatisticsOffsetHelper
    {
        public static bool HasOffsetChanged(DateTimeOffset sourceLocalStart, TimeSpan currentOffset)
        {
            return sourceLocalStart.Offset != currentOffset;
        }
    }
}
