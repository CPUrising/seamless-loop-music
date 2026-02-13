using System;
using System.Collections.Generic;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 播放队列服务
    /// 负责管理播放列表、当前索引、播放模式以及切歌逻辑
    /// </summary>
    public class PlaybackQueueService
    {
        private readonly Random _random = new Random();
        
        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        public int CurrentIndex { get; set; } = -1;
        public PlayMode CurrentMode { get; set; } = PlayMode.SingleLoop;
        public int LoopLimit { get; set; } = 1;
        
        public event Action<int> OnIndexChanged;

        public MusicTrack GetCurrentTrack()
        {
            if (Playlist == null || CurrentIndex < 0 || CurrentIndex >= Playlist.Count)
                return null;
            return Playlist[CurrentIndex];
        }

        public string GetNextTrackPath()
        {
            if (Playlist == null || Playlist.Count == 0) return null;

            int nextIndex;
            if (CurrentMode == PlayMode.Shuffle)
            {
                nextIndex = _random.Next(0, Playlist.Count);
            }
            else
            {
                nextIndex = (CurrentIndex + 1) % Playlist.Count;
            }

            CurrentIndex = nextIndex;
            OnIndexChanged?.Invoke(nextIndex);
            return Playlist[nextIndex].FilePath;
        }

        public string GetPreviousTrackPath()
        {
            if (Playlist == null || Playlist.Count == 0) return null;

            int prevIndex = (CurrentIndex - 1 + Playlist.Count) % Playlist.Count;
            CurrentIndex = prevIndex;
            OnIndexChanged?.Invoke(prevIndex);
            return Playlist[prevIndex].FilePath;
        }

        public string GetTrackPathAtIndex(int index)
        {
            if (Playlist == null || index < 0 || index >= Playlist.Count) return null;

            CurrentIndex = index;
            OnIndexChanged?.Invoke(index);
            return Playlist[index].FilePath;
        }

        public int FindIndexByPath(string filePath)
        {
            if (Playlist == null) return -1;
            return Playlist.FindIndex(t => t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        }
    }
}
