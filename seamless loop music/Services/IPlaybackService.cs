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
        TimeSpan TotalTime { get; }
        int SampleRate { get; }
        float Volume { get; set; }
        
        event Action<MusicTrack> TrackChanged;
        event Action<PlaybackState> StateChanged;
        event Action<TimeSpan> PositionChanged;

        Task LoadTrackAsync(MusicTrack track, bool autoPlay = false);
        void Play();
        void Pause();
        void Stop();
        void Seek(TimeSpan position);
        void SeekToSample(long sample);
        
        void SetLoopPoints(long startSample, long endSample);
        Task<(long Start, long End)> FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart);
    }
}

