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
        int CreatePlaylist(string name);
        void RenamePlaylist(int playlistId, string newName);
        void DeletePlaylist(int playlistId);
        
        void AddTrackToPlaylist(int playlistId, MusicTrack track);
        void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks);
        void RemoveTrackFromPlaylist(int playlistId, int songId);
        
        void AddFilesToPlaylist(int playlistId, string[] filePaths);
        
        List<MusicTrack> LoadPlaylistFromDb(int playlistId);
        
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);
        
        void SetLoopStart(long sample);
        void SetLoopEnd(long sample);
        void ApplyLoopCandidate(LoopCandidate candidate);
        void ResetABLoopPoints();
        
        float Volume { get; set; }
        bool IsSeamlessLoopEnabled { get; set; }
        bool IsFeatureLoopEnabled { get; set; }
        double MatchWindowSize { get; set; }
        double MatchSearchRadius { get; set; }
        
        Task<List<LoopCandidate>> GetLoopCandidatesAsync();
        Task<int> CheckPyMusicLooperStatusAsync();
        Task UpdateTrackLoopCandidatesAsync(MusicTrack track, List<LoopCandidate> candidates);
        Task AnalyzeTracksAsync(IEnumerable<MusicTrack> tracks, IProgress<(int current, int total, string fileName)> progress = null);

        // 音乐文件夹管理
        List<string> GetMusicFolders();
        void AddMusicFolder(string folderPath);
        void RemoveMusicFolder(string folderPath);
        Task ScanMusicFoldersAsync();
        Task<(int tracks, int playlists)> SyncDatabaseAsync(string externalDbPath);

        new void Dispose();
    }
}
