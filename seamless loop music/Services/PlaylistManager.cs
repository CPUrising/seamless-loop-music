using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public class PlaylistManager : IPlaylistManager
    {
        private readonly IPlaylistRepository _playlistRepository;
        private readonly ITrackRepository _trackRepository;

        public PlaylistManager(IPlaylistRepository playlistRepository, ITrackRepository trackRepository)
        {
            _playlistRepository = playlistRepository;
            _trackRepository = trackRepository;
        }

        public async Task<List<Playlist>> GetAllPlaylistsAsync()
        {
            return await _playlistRepository.GetAllAsync();
        }

        public async Task<Playlist> CreatePlaylistAsync(string name)
        {
            int id = _playlistRepository.Add(name);
            var playlists = await _playlistRepository.GetAllAsync();
            return playlists.FirstOrDefault(p => p.Id == id);
        }

        public async Task DeletePlaylistAsync(int playlistId)
        {
            await Task.Run(() => _playlistRepository.Delete(playlistId));
        }

        public async Task AddTrackToPlaylistAsync(int playlistId, MusicTrack track)
        {
            await Task.Run(() => _playlistRepository.AddTrack(playlistId, track.Id));
        }

        public async Task RemoveTrackFromPlaylistAsync(int playlistId, int trackId)
        {
            await Task.Run(() => _playlistRepository.RemoveTrack(playlistId, trackId));
        }

        public async Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId)
        {
            return await _playlistRepository.GetTracksInPlaylistAsync(playlistId);
        }
    }
}
