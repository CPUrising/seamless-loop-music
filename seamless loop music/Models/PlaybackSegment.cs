namespace seamless_loop_music.Models
{
    public sealed class PlaybackSegment
    {
        public PlaybackSegment(string segmentId, int trackId, long startedAtUtcMs, long durationMs)
        {
            SegmentId = segmentId;
            TrackId = trackId;
            StartedAtUtcMs = startedAtUtcMs;
            DurationMs = durationMs;
        }

        public string SegmentId { get; private set; }
        public int TrackId { get; private set; }
        public long StartedAtUtcMs { get; private set; }
        public long DurationMs { get; private set; }
    }
}
