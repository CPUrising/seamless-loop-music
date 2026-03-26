using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;
using NAudio.Wave;
using Prism.Mvvm;
using Prism.Regions;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
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

        public PlaybackService(ITrackRepository trackRepository, IEventAggregator eventAggregator)
        {
            _trackRepository = trackRepository;
            _eventAggregator = eventAggregator;
            
            _audioLooper = new AudioLooper();
            
            // 转发底层事件
            _audioLooper.OnPlayStateChanged += state => 
            {
                _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Publish(state);
                StateChanged?.Invoke(state);
            };
            
            _audioLooper.OnPositionChanged += pos => 
            {
                PositionChanged?.Invoke(pos);
            };

            _audioLooper.OnAudioLoaded += (samples, rate) => 
            {
                if (CurrentTrack != null)
                {
                    _audioLooper.SetLoopStartSample(CurrentTrack.LoopStart);
                    _audioLooper.SetLoopEndSample(CurrentTrack.LoopEnd);
                    _eventAggregator.GetEvent<TrackLoadedEvent>().Publish(CurrentTrack);
                    TrackChanged?.Invoke(CurrentTrack);
                }
            };
        }

        public async Task LoadTrackAsync(MusicTrack track, bool autoPlay = false)
        {
            if (track == null) return;
            
            // 尝试从库中获取最新数据（或者如果没有 ID 则先插入）
            var dbTrack = await _trackRepository.GetByPathAsync(track.FilePath);
            if (dbTrack == null)
            {
                await _trackRepository.AddAsync(track);
                dbTrack = track;
            }
            
            CurrentTrack = dbTrack;
            
            // 加载音频
            _audioLooper.LoadAudio(dbTrack.FilePath);
            
            if (autoPlay) Play();
        }

        public void Play() => _audioLooper.Play();
        public void Pause() => _audioLooper.Pause();
        public void Stop() => _audioLooper.Stop();
        
        public void Seek(TimeSpan position)
        {
            double percent = position.TotalSeconds / TotalTime.TotalSeconds;
            _audioLooper.Seek(percent);
        }

        public void SeekToSample(long sample) => _audioLooper.SeekToSample(sample);

        public void SetLoopPoints(long startSample, long endSample)
        {
            _audioLooper.SetLoopStartSample(startSample);
            _audioLooper.SetLoopEndSample(endSample);
            
            if (CurrentTrack != null)
            {
                CurrentTrack.LoopStart = startSample;
                CurrentTrack.LoopEnd = endSample;
                _trackRepository.UpdateAsync(CurrentTrack).Wait(); 
            }
            
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Publish((startSample, endSample));
        }

        public Task<(long Start, long End)> FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart)
        {
            var tcs = new TaskCompletionSource<(long, long)>();
            _audioLooper.FindBestLoopPointsAsync(currentStart, currentEnd, adjustStart, (s, e) => 
            {
                tcs.SetResult((s, e));
            });
            return tcs.Task;
        }

        public void Dispose()
        {
            _audioLooper?.Dispose();
        }
    }
}
