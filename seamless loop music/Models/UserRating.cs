using System;

namespace seamless_loop_music.Models
{
    public class UserRating
    {
        public int Id { get; set; }
        public int TrackId { get; set; }
        public int Rating { get; set; }
        public bool IsLoved { get; set; }
        public DateTime LastModified { get; set; } = DateTime.Now;
    }
}
