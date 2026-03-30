using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IPlayerService : IDisposable
    {
        List<MusicTrack> Playlist { get; set; }
        int CurrentIndex { get; set; }
        MusicTrack CurrentTrack { get; }
        PlaybackState PlaybackState { get; }
        
        // Loop Point Properties
        long LoopStartSample { get; set; }
        long LoopEndSample { get; set; }
        int SampleRate { get; }

        void Play();
        void Pause();
        void Stop();
        void Seek(double percent);
        void SeekToSample(long sample);
        
        List<Playlist> GetAllPlaylists();
        int CreatePlaylist(string name, string folderPath = null, bool isLinked = false);
        void RenamePlaylist(int playlistId, string newName);
        void DeletePlaylist(int playlistId);
        
        void AddTrackToPlaylist(int playlistId, MusicTrack track);
        void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks);
        void RemoveTrackFromPlaylist(int playlistId, int songId);
        
        Task AddFilesToPlaylist(int playlistId, string[] filePaths);
        Task AddFolderToPlaylist(int playlistId, string folderPath);
        Task RemoveFolderFromPlaylist(int playlistId, string folderPath);
        List<string> GetPlaylistFolders(int playlistId);
        
        Task RefreshPlaylist(int playlistId);
        List<MusicTrack> LoadPlaylistFromDb(int playlistId);
        
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);
        
        void SetLoopStart(long sample);
        void SetLoopEnd(long sample);
        void ApplyLoopCandidate(LoopCandidate candidate);
        void ResetABLoopPoints();
        
        float Volume { get; set; }
        double MatchWindowSize { get; set; }
        double MatchSearchRadius { get; set; }
        
        Task<List<LoopCandidate>> GetLoopCandidatesAsync();
        Task<int> CheckPyMusicLooperStatusAsync();
        Task UpdateTrackLoopCandidatesAsync(MusicTrack track, List<LoopCandidate> candidates);
        void Dispose();
    }
}
