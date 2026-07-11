using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace SeamlessLoop.Tests
{
    internal sealed class PlaybackServiceTestDouble : IPlaybackService
    {
        public int CheckpointCount { get; private set; }
        public int RotateCount { get; private set; }
        public List<string> Lifecycle { get; } = new List<string>();
        public bool FailCheckpoint { get; set; }
        public Func<Task<bool>>? RotateFunc { get; set; }

        public MusicTrack? CurrentTrack { get; }
        public PlaybackState PlaybackState => PlaybackState.Stopped;
        public TimeSpan CurrentTime => TimeSpan.Zero;
        public long CurrentSample => 0;
        public TimeSpan TotalTime => TimeSpan.Zero;
        public int SampleRate => 44100;
        public bool IsABFusionLoaded => false;
        public float Volume { get; set; }
        public bool IsSeamlessLoopEnabled { get; set; }
        public bool IsFeatureLoopEnabled { get; set; }
        public double MatchWindowSize { get; set; }
        public double MatchSearchRadius { get; set; }
        public IReadOnlyList<MusicTrack> Queue { get; } = new List<MusicTrack>();
        public int CurrentIndex => -1;
        public PlayMode PlayMode { get; set; }
        public CategoryItem? CurrentCategory { get; }

        public event Action<MusicTrack>? TrackChanged;
        public event Action<PlaybackState>? StateChanged;
        public event Action<float>? VolumeChanged;
        public event Action<PlayMode>? PlayModeChanged;
        public event Action<bool>? SeamlessLoopChanged;
        public event Action<bool>? FeatureLoopChanged;
        public event Action? QueueChanged;

        public Task LoadTrackAsync(MusicTrack track, bool autoPlay = false) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Next() { }
        public void Previous() { }
        public void Seek(TimeSpan position) { }
        public void SeekToSample(long sample) { }
        public void SetLoopPoints(long startSample, long endSample) { }
        public void ResetABLoopPoints() { }
        public Task<(long Start, long End)> FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart) => Task.FromResult((currentStart, currentEnd));
        public Task EnqueueArtistAsync(string artistName) => Task.CompletedTask;
        public Task EnqueueAlbumAsync(string albumName) => Task.CompletedTask;
        public Task EnqueuePlaylistAsync(CategoryItem playlistItem) => Task.CompletedTask;
        public void SetQueue(IEnumerable<MusicTrack> tracks, MusicTrack currentTrack = null, CategoryItem category = null) { }
        public void AddToQueue(MusicTrack track) { }
        public void RemoveFromQueue(int index) { }
        public void ClearQueue() { }
        public void MoveQueueItem(int fromIndex, int toIndex) { }
        public Task FlushPlaybackStatisticsAsync() => Task.CompletedTask;

        public async Task<T> CapturePlaybackStatisticsCheckpointAsync<T>(Func<Task<T>> captureAsync)
        {
            CheckpointCount++;
            Lifecycle.Add("capture");
            if (FailCheckpoint) throw new InvalidOperationException("checkpoint failed");
            return await captureAsync();
        }

        public Task PersistPendingPlaybackStatisticsAsync() => Task.CompletedTask;
        public void ResumePlaybackStatisticsAfterFailedFlush() { }
        public Task<int> ClearPlaybackStatisticsAsync() => Task.FromResult(0);

        public async Task<bool> RotateIfCurrentGenerationTombstonedAsync()
        {
            RotateCount++;
            Lifecycle.Add("rotate");
            return RotateFunc == null ? false : await RotateFunc();
        }

        public void Dispose() { }
    }
}
