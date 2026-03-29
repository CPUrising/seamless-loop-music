using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Events;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Events;


namespace seamless_loop_music.Services
{
    public class PlaylistManager : IPlaylistManager
    {
        private readonly IPlaylistRepository _playlistRepository;
        private readonly ITrackRepository _trackRepository;
        private readonly IEventAggregator _eventAggregator;

        public PlaylistManager(IPlaylistRepository playlistRepository, ITrackRepository trackRepository, IEventAggregator eventAggregator)
        {
            _playlistRepository = playlistRepository;
            _trackRepository = trackRepository;
            _eventAggregator = eventAggregator;
        }

        public async Task<List<Playlist>> GetAllPlaylistsAsync()
        {
            return await _playlistRepository.GetAllAsync();
        }

        public async Task<Playlist> CreatePlaylistAsync(string name)
        {
            int id = _playlistRepository.Add(name);
            _eventAggregator.GetEvent<PlaylistChangedEvent>().Publish();
            var playlists = await _playlistRepository.GetAllAsync();
            return playlists.FirstOrDefault(p => p.Id == id);
        }

        public async Task DeletePlaylistAsync(int playlistId)
        {
            await Task.Run(() => _playlistRepository.Delete(playlistId));
            _eventAggregator.GetEvent<PlaylistChangedEvent>().Publish();
        }

        public async Task RenamePlaylistAsync(int id, string name)
        {
            await Task.Run(() => _playlistRepository.Rename(id, name));
            _eventAggregator.GetEvent<PlaylistChangedEvent>().Publish();
        }

        public async Task AddTrackToPlaylistAsync(int playlistId, MusicTrack track)
        {
            await Task.Run(() => _playlistRepository.AddTrack(playlistId, track.Id));
        }

        public async Task RemoveTrackFromPlaylistAsync(int playlistId, int trackId)
        {
            await Task.Run(() => _playlistRepository.RemoveTrack(playlistId, trackId));
        }

        private List<MusicTrack> _nowPlayingList = new List<MusicTrack>();
        private int _currentIndex = -1;

        public async Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId)
        {
            return await _playlistRepository.GetTracksInPlaylistAsync(playlistId);
        }

        public void SetNowPlayingList(IEnumerable<MusicTrack> tracks, MusicTrack current)
        {
            _nowPlayingList = tracks?.ToList() ?? new List<MusicTrack>();
            _currentIndex = _nowPlayingList.FindIndex(t => t.Id == current?.Id);
        }

        public MusicTrack GetNextTrack()
        {
            if (_nowPlayingList.Count == 0) return null;
            _currentIndex = (_currentIndex + 1) % _nowPlayingList.Count;
            return _nowPlayingList[_currentIndex];
        }

        public MusicTrack GetPreviousTrack()
        {
            if (_nowPlayingList.Count == 0) return null;
            _currentIndex = (_currentIndex - 1 + _nowPlayingList.Count) % _nowPlayingList.Count;
            return _nowPlayingList[_currentIndex];
        }
    }
}
