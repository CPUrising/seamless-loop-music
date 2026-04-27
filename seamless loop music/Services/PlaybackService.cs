using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Events;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Services;

namespace seamless_loop_music.Services
{
    public class PlaybackService : IPlaybackService
    {
        private readonly AudioLooper _audioLooper;
        private readonly ITrackRepository _trackRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaylistManager _playlistManager;
        private readonly IQueueManager _queueManager;
        private readonly TrackMetadataService _metadataService;

        public MusicTrack CurrentTrack { get; private set; }
        public PlaybackState PlaybackState => _audioLooper?.PlaybackState ?? PlaybackState.Stopped;
        public TimeSpan CurrentTime => _audioLooper?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _audioLooper?.TotalTime ?? TimeSpan.Zero;
        public int SampleRate => _audioLooper?.SampleRate ?? 44100;
        public bool IsABFusionLoaded => _audioLooper?.IsABFusionLoaded ?? false;
        public float Volume { get => _audioLooper.Volume; set => _audioLooper.Volume = value; }
        public bool IsSeamlessLoopEnabled { get => _audioLooper.IsSeamlessLoopEnabled; set => _audioLooper.IsSeamlessLoopEnabled = value; }
        public bool IsFeatureLoopEnabled { get => _audioLooper.IsFeatureLoopEnabled; set => _audioLooper.IsFeatureLoopEnabled = value; }
        public double MatchWindowSize { get => _audioLooper.MatchWindowSize; set => _audioLooper.MatchWindowSize = value; }
        public double MatchSearchRadius { get => _audioLooper.MatchSearchRadius; set => _audioLooper.MatchSearchRadius = value; }

        public IReadOnlyList<MusicTrack> Queue => _queueManager.Queue;
        public int CurrentIndex => _queueManager.CurrentIndex;

        public event Action<MusicTrack> TrackChanged;
        public event Action<PlaybackState> StateChanged;
        public event Action QueueChanged;

        public PlaybackService(ITrackRepository trackRepository, IPlaylistManager playlistManager, IEventAggregator eventAggregator, IQueueManager queueManager, TrackMetadataService metadataService)
        {
            _trackRepository = trackRepository;
            _playlistManager = playlistManager;
            _eventAggregator = eventAggregator;
            _queueManager = queueManager;
            _metadataService = metadataService;
            _audioLooper = new AudioLooper();

            _audioLooper.OnPlayStateChanged += state =>
            {
                _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Publish(state);
                StateChanged?.Invoke(state);
            };
            
            _audioLooper.OnStatusChanged += msg =>
            {
                _eventAggregator.GetEvent<StatusMessageEvent>().Publish(msg);
            };

            _queueManager.QueueChanged += () =>
            {
                QueueChanged?.Invoke();
            };
        }

        public async Task LoadTrackAsync(MusicTrack track, bool autoPlay = false)
        {
            if (track == null) return;

            // 如果曲目没变且已经在播放，则不需要重新加载音频流，避免打断
            bool isAlreadyLoaded = CurrentTrack != null && CurrentTrack.Id == track.Id;
            
            CurrentTrack = track;

            if (!isAlreadyLoaded)
            {
                string partB = _metadataService.FindPartB(track.FilePath);
                await Task.Run(() => _audioLooper.LoadAudio(track.FilePath, partB));

                // 核心逻辑：如果是 A/B 融合模式，且数据库记录的长度与实际拼接后的长度差异显著
                if (_audioLooper.IsABFusionLoaded && (track.LoopEnd <= 0 || Math.Abs(track.TotalSamples - _audioLooper.TotalSamples) > 10000))
                {
                    track.LoopStart = _audioLooper.LoopStartSample;
                    track.LoopEnd = _audioLooper.LoopEndSample;
                    track.TotalSamples = _audioLooper.TotalSamples;
                    _trackRepository.UpdateLoopPoints(track.Id, track.LoopStart, track.LoopEnd);
                }
                else
                {
                    _audioLooper.SetLoopStartSample(track.LoopStart);
                    _audioLooper.SetLoopEndSample(track.LoopEnd);
                }
            }

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
            try
            {
                var next = _queueManager.GetNextTrack();
                if (next != null) await LoadTrackAsync(next, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Next失败] {ex.Message}");
            }
        }

        public async void Previous()
        {
            try
            {
                var prev = _queueManager.GetPreviousTrack();
                if (prev != null) await LoadTrackAsync(prev, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Previous失败] {ex.Message}");
            }
        }

        public void Seek(TimeSpan position) => _audioLooper.Seek(position.TotalSeconds / TotalTime.TotalSeconds);
        public void SeekToSample(long sample) => _audioLooper.SeekToSample(sample);

        public void SetLoopPoints(long startSample, long endSample)
        {
            _audioLooper.SetLoopPoints(startSample, endSample);
            
            if (CurrentTrack != null)
            {
                CurrentTrack.LoopStart = startSample;
                CurrentTrack.LoopEnd = endSample;
                _trackRepository.UpdateLoopPoints(CurrentTrack.Id, startSample, endSample);
            }
            
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Publish((startSample, endSample));
        }

        public void ResetABLoopPoints()
        {
            _audioLooper.ResetABLoopPoints();
            if (CurrentTrack != null)
            {
                CurrentTrack.LoopStart = _audioLooper.LoopStartSample;
                CurrentTrack.LoopEnd = _audioLooper.LoopEndSample;
                _trackRepository.UpdateLoopPoints(CurrentTrack.Id, CurrentTrack.LoopStart, CurrentTrack.LoopEnd);
            }
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Publish((_audioLooper.LoopStartSample, _audioLooper.LoopEndSample));
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

        public async Task EnqueueArtistAsync(string artistName)
        {
            var tracks = await _trackRepository.GetByArtistAsync(artistName);
            if (tracks != null && tracks.Any())
            {
                _queueManager.SetQueue(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public async Task EnqueueAlbumAsync(string albumName)
        {
            var tracks = await _trackRepository.GetByAlbumAsync(albumName);
            if (tracks != null && tracks.Any())
            {
                _queueManager.SetQueue(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public async Task EnqueuePlaylistAsync(CategoryItem playlistItem)
        {
            List<MusicTrack> tracks = null;
            if (playlistItem.Id == -1)
            {
                tracks = await _trackRepository.GetAllAsync();
            }
            else if (playlistItem.Id == -2)
            {
                tracks = await _trackRepository.GetLovedTracksAsync();
            }
            else if (playlistItem.Id > 0)
            {
                tracks = await _playlistManager.GetTracksInPlaylistAsync(playlistItem.Id);
            }

            if (tracks != null && tracks.Any())
            {
                _queueManager.SetQueue(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public void SetQueue(IEnumerable<MusicTrack> tracks, MusicTrack currentTrack = null)
        {
            _queueManager.SetQueue(tracks, currentTrack);
        }

        public void AddToQueue(MusicTrack track)
        {
            _queueManager.AddToQueue(track);
        }

        public void RemoveFromQueue(int index)
        {
            _queueManager.RemoveFromQueue(index);
        }

        public void ClearQueue()
        {
            _queueManager.ClearQueue();
        }

        public void MoveQueueItem(int fromIndex, int toIndex)
        {
            _queueManager.MoveTo(fromIndex, toIndex);
        }

        public void Dispose()
        {
            _audioLooper?.Dispose();
        }
    }
}
