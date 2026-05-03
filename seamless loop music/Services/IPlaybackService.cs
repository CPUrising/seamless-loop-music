using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IPlaybackService : IDisposable
    {
        MusicTrack CurrentTrack { get; }
        PlaybackState PlaybackState { get; }
        TimeSpan CurrentTime { get; }
        long CurrentSample { get; }
        TimeSpan TotalTime { get; }
        int SampleRate { get; }
        bool IsABFusionLoaded { get; }
        float Volume { get; set; }
        bool IsSeamlessLoopEnabled { get; set; }
        bool IsFeatureLoopEnabled { get; set; }
        double MatchWindowSize { get; set; }
        double MatchSearchRadius { get; set; }

        IReadOnlyList<MusicTrack> Queue { get; }
        int CurrentIndex { get; }
        PlayMode PlayMode { get; set; }
        CategoryItem CurrentCategory { get; }

        event Action<MusicTrack> TrackChanged;
        event Action<PlaybackState> StateChanged;
        event Action<float> VolumeChanged;
        event Action<PlayMode> PlayModeChanged;
        event Action<bool> SeamlessLoopChanged;
        event Action<bool> FeatureLoopChanged;
        event Action QueueChanged;

        Task LoadTrackAsync(MusicTrack track, bool autoPlay = false);
        void Play();
        void Pause();
        void Stop();
        void Next();
        void Previous();
        void Seek(TimeSpan position);
        void SeekToSample(long sample);
        
        void SetLoopPoints(long startSample, long endSample);
        void ResetABLoopPoints();
        Task<(long Start, long End)> FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart);

        Task EnqueueArtistAsync(string artistName);
        Task EnqueueAlbumAsync(string albumName, string artistName = null);
        Task EnqueuePlaylistAsync(CategoryItem playlistItem);

        void SetQueue(IEnumerable<MusicTrack> tracks, MusicTrack currentTrack = null, CategoryItem category = null);
        void AddToQueue(MusicTrack track);
        void RemoveFromQueue(int index);
        void ClearQueue();
        void MoveQueueItem(int fromIndex, int toIndex);
    }
}

