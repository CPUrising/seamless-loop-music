using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IPlayerService : IDisposable
    {
        // 属性
        List<MusicTrack> Playlist { get; set; }
        int CurrentIndex { get; set; }
        PlayMode CurrentMode { get; set; }
        int LoopLimit { get; set; }
        MusicTrack CurrentTrack { get; }
        bool IsABMode { get; }
        
        PlaybackState PlaybackState { get; }
        TimeSpan CurrentTime { get; }
        TimeSpan TotalTime { get; }
        long LoopStartSample { get; }
        long LoopEndSample { get; }
        int SampleRate { get; }
        double MatchWindowSize { get; set; }
        double MatchSearchRadius { get; set; }
        float Volume { get; set; }

        // 事件
        event Action<MusicTrack> OnTrackLoaded;
        event Action<PlaybackState> OnPlayStateChanged;
        event Action<string> OnStatusMessage;
        event Action<int> OnIndexChanged;
        event Action<long, long> OnLoopPointsChanged;

        // 播放控制
        void Play();
        void Pause();
        void Stop();
        void Seek(double percent);
        void SeekToSample(long sample);

        // 曲目管理
        void LoadTrack(string filePath, bool autoPlay = false);
        void PlayAtIndex(int index);
        void Next();
        void Previous();
        void SaveCurrentTrack();
        void RenameCurrentTrack(string newName);

        // 循环点分析与匹配
        void SmartMatchLoopReverseAsync(Action onComplete = null);
        void SmartMatchLoopForwardAsync(Action onComplete = null);
        void SmartMatchLoopExternalAsync(Action onComplete = null);
        Task<List<LoopCandidate>> GetLoopCandidatesAsync(bool forceRefresh = false);
        void ApplyLoopCandidate(LoopCandidate candidate);
        Task BatchSmartMatchLoopExternalAsync(List<MusicTrack> tracks, Action<int, int, string> onProgress, Action onComplete);
        void ResetABLoopPoints();

        // 歌单操作透传
        List<PlaylistFolder> GetAllPlaylists();
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
        MusicTrack GetStoredTrackInfo(string filePath);
        void UpdateOfflineTrack(MusicTrack track);
        long GetTotalSamples(string filePath);
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);
        void SetLoopStart(long sample);
        void SetLoopEnd(long sample);
        void ImportTracks(IEnumerable<MusicTrack> tracks);

        // 设置
        void SetPyMusicLooperCachePath(string path);
        void SetPyMusicLooperExecutablePath(string path);
        Task<int> CheckPyMusicLooperStatusAsync();
    }
}
