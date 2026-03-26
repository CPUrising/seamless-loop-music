using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;
using seamless_loop_music.Data.Repositories;

namespace seamless_loop_music.Services
{
    public class PlaylistManager : IPlaylistManager
    {
        private readonly IPlaylistRepository _playlistRepository;

        public PlaylistManager(IPlaylistRepository playlistRepository)
        {
            _playlistRepository = playlistRepository;
        }

        public async Task<List<Playlist>> GetAllPlaylistsAsync()
        {
            return await _playlistRepository.GetAllAsync();
        }

        public async Task<Playlist> CreatePlaylistAsync(string name)
        {
            var playlist = new Playlist { Name = name, CreatedAt = DateTime.Now };
            await _playlistRepository.AddAsync(playlist);
            return playlist;
        }

        public async Task DeletePlaylistAsync(int playlistId)
        {
            await _playlistRepository.DeleteAsync(playlistId);
        }

        public async Task AddTrackToPlaylistAsync(int playlistId, MusicTrack track)
        {
            await _playlistRepository.AddTrackToPlaylistAsync(playlistId, track.Id);
        }

        public async Task RemoveTrackFromPlaylistAsync(int playlistId, int trackId)
        {
            await _playlistRepository.RemoveTrackFromPlaylistAsync(playlistId, trackId);
        }

        public async Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId)
        {
            return await _playlistRepository.GetTracksInPlaylistAsync(playlistId);
        }
    }
}
