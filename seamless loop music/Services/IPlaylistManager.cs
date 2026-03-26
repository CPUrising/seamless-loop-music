using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IPlaylistManager
    {
        Task<List<Playlist>> GetAllPlaylistsAsync();
        Task<Playlist> CreatePlaylistAsync(string name);
        Task DeletePlaylistAsync(int playlistId);
        Task AddTrackToPlaylistAsync(int playlistId, MusicTrack track);
        Task RemoveTrackFromPlaylistAsync(int playlistId, int trackId);
        Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId);
    }
}

