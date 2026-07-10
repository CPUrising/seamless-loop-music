using System.IO;

namespace seamless_loop_music.Models
{
    public static class TrackCoverResolver
    {
        public static string Resolve(string trackCoverPath, string albumCoverPath, string artistCoverPath)
        {
            if (!string.IsNullOrEmpty(trackCoverPath) && File.Exists(trackCoverPath)) return trackCoverPath;
            if (!string.IsNullOrEmpty(albumCoverPath) && File.Exists(albumCoverPath)) return albumCoverPath;
            if (!string.IsNullOrEmpty(artistCoverPath) && File.Exists(artistCoverPath)) return artistCoverPath;
            return null;
        }
    }
}
