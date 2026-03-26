using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using seamless_loop_music.Models;
using seamless_loop_music.Data;

namespace seamless_loop_music.Services
{
    public class PlayerService : IPlayerService
    {
        private readonly IDatabaseHelper _databaseHelper;
        private readonly IPlaybackService _playbackService;

        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        public int CurrentIndex { get; set; } = -1;
        public MusicTrack CurrentTrack => _playbackService.CurrentTrack;
        public PlaybackState PlaybackState => _playbackService.PlaybackState;
        
        public float Volume { get => _playbackService.Volume; set => _playbackService.Volume = value; }
        public double MatchWindowSize { get; set; } = 1.0;
        public double MatchSearchRadius { get; set; } = 5.0;

        public PlayerService(IDatabaseHelper databaseHelper, IPlaybackService playbackService)
        {
            _databaseHelper = databaseHelper;
            _playbackService = playbackService;
        }

        public void Play() => _playbackService.Play();
        public void Pause() => _playbackService.Pause();
        public void Stop() => _playbackService.Stop();
        public void Seek(double percent) => _playbackService.Seek(TimeSpan.FromSeconds(percent * _playbackService.TotalTime.TotalSeconds));
        public void SeekToSample(long sample) => _playbackService.SeekToSample(sample);

        public List<Playlist> GetAllPlaylists() => _databaseHelper.GetAllPlaylists();
        public int CreatePlaylist(string name, string folderPath = null, bool isLinked = false) => _databaseHelper.AddPlaylist(name, folderPath, isLinked);
        public void RenamePlaylist(int playlistId, string newName) => _databaseHelper.RenamePlaylist(playlistId, newName);
        public void DeletePlaylist(int playlistId) => _databaseHelper.DeletePlaylist(playlistId);

        public void AddTrackToPlaylist(int playlistId, MusicTrack track) => _databaseHelper.AddSongToPlaylist(playlistId, track.Id);
        public void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks)
        {
            foreach(var t in tracks) AddTrackToPlaylist(playlistId, t);
        }
        public void RemoveTrackFromPlaylist(int playlistId, int songId) => _databaseHelper.RemoveSongFromPlaylist(playlistId, songId);

        public Task AddFilesToPlaylist(int playlistId, string[] filePaths) => Task.CompletedTask; // TODO
        public Task AddFolderToPlaylist(int playlistId, string folderPath) => Task.CompletedTask; // TODO
        public Task RemoveFolderFromPlaylist(int playlistId, string folderPath) => Task.CompletedTask; // TODO
        
        public List<string> GetPlaylistFolders(int playlistId) => _databaseHelper.GetPlaylists(playlistId);
        
        public Task RefreshPlaylist(int playlistId) => Task.CompletedTask;
        public List<MusicTrack> LoadPlaylistFromDb(int playlistId) => _databaseHelper.GetPlaylistTracks(playlistId).ToList();

        public void UpdatePlaylistsSortOrder(List<int> playlistIds) => _databaseHelper.UpdatePlaylistsSortOrder(playlistIds);
        public void UpdateTracksSortOrder(int playlistId, List<int> songIds) => _databaseHelper.UpdateTracksSortOrder(playlistId, songIds);

        public void SetLoopStart(long sample) => _playbackService.SetLoopPoints(sample, _playbackService.CurrentTrack?.LoopEnd ?? 0);
        public void SetLoopEnd(long sample) => _playbackService.SetLoopPoints(_playbackService.CurrentTrack?.LoopStart ?? 0, sample);
        public void ResetABLoopPoints() => _playbackService.SetLoopPoints(0, 0);

        public async Task<List<LoopCandidate>> GetLoopCandidatesAsync() => new List<LoopCandidate>();

        public void Dispose() { }
    }
}
