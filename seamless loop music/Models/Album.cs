using System;

namespace seamless_loop_music.Models
{
    public class Album
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ArtistId { get; set; }
        public string CoverPath { get; set; }
    }
}
