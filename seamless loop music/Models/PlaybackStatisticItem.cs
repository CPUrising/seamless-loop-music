namespace seamless_loop_music.Models
{
    public class PlaybackStatisticItem
    {
        public int TrackId { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string TrackCoverPath { get; set; }
        public string AlbumCoverPath { get; set; }
        public string ArtistCoverPath { get; set; }
        public long TotalDurationMs { get; set; }

    }
}
