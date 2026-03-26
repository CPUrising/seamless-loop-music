using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Events;
using seamless_loop_music.Data.Repositories;

namespace seamless_loop_music.Services
{
    public class PlaybackService : IPlaybackService
    {
        private readonly AudioLooper _audioLooper;
        private readonly ITrackRepository _trackRepository;
        private readonly IEventAggregator _eventAggregator;

        public MusicTrack CurrentTrack { get; private set; }
        public PlaybackState PlaybackState => _audioLooper.PlaybackState;
        public TimeSpan CurrentTime => _audioLooper.CurrentTime;
        public TimeSpan TotalTime => _audioLooper.TotalTime;
        public int SampleRate => _audioLooper.SampleRate;
        public float Volume { get => _audioLooper.Volume; set => _audioLooper.Volume = value; }

        public event Action<MusicTrack> TrackChanged;
        public event Action<PlaybackState> StateChanged;
        public event Action<TimeSpan> PositionChanged;

        private readonly IPlaylistManager _playlistManager;

        public PlaybackService(ITrackRepository trackRepository, IPlaylistManager playlistManager, IEventAggregator eventAggregator)
        {
            _trackRepository = trackRepository;
            _playlistManager = playlistManager;
            _eventAggregator = eventAggregator;
            _audioLooper = new AudioLooper();

            _audioLooper.OnPlayStateChanged += state =>
            {
                _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Publish(state);
                StateChanged?.Invoke(state);
            };

            _audioLooper.OnPositionChanged += pos =>
            {
                PositionChanged?.Invoke(pos);
            };
        }

        public async Task LoadTrackAsync(MusicTrack track, bool autoPlay = false)
        {
            if (track == null) return;

            CurrentTrack = track;
            await Task.Run(() => _audioLooper.LoadAudio(track.FilePath));
            
            _audioLooper.SetLoopStartSample(track.LoopStart);
            _audioLooper.SetLoopEndSample(track.LoopEnd);

            TrackChanged?.Invoke(track);
            _eventAggregator.GetEvent<TrackLoadedEvent>().Publish(track);

            if (autoPlay)
            {
                Play();
            }
        }

        public void Play() => _audioLooper.Play();
        public void Pause() => _audioLooper.Pause();
        public void Stop() => _audioLooper.Stop();
        
        public async void Next()
        {
            var next = _playlistManager.GetNextTrack();
            if (next != null) await LoadTrackAsync(next, true);
        }

        public async void Previous()
        {
            var prev = _playlistManager.GetPreviousTrack();
            if (prev != null) await LoadTrackAsync(prev, true);
        }

        public void Seek(TimeSpan position) => _audioLooper.Seek(position.TotalSeconds);
        public void SeekToSample(long sample) => _audioLooper.SeekToSample(sample);

        public void SetLoopPoints(long startSample, long endSample)
        {
            _audioLooper.SetLoopStartSample(startSample);
            _audioLooper.SetLoopEndSample(endSample);
            
            if (CurrentTrack != null)
            {
                CurrentTrack.LoopStart = startSample;
                CurrentTrack.LoopEnd = endSample;
                _trackRepository.UpdateLoopPoints(CurrentTrack.Id, startSample, endSample);
            }
            
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Publish((startSample, endSample));
        }

        public async Task<(long Start, long End)> FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart)
        {
            var tcs = new TaskCompletionSource<(long, long)>();
            _audioLooper.FindBestLoopPointsAsync(currentStart, currentEnd, adjustStart, (start, end) => 
            {
                tcs.SetResult((start, end));
            });
            return await tcs.Task;
        }

        public void Dispose()
        {
            _audioLooper?.Dispose();
        }
    }
}
