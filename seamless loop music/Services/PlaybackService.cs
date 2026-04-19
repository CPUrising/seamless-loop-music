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

namespace seamless_loop_music.Services
{
    public class PlaybackService : IPlaybackService
    {
        private readonly AudioLooper _audioLooper;
        private readonly ITrackRepository _trackRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaylistManager _playlistManager;

        public MusicTrack CurrentTrack { get; private set; }
        public PlaybackState PlaybackState => _audioLooper?.PlaybackState ?? PlaybackState.Stopped;
        public TimeSpan CurrentTime => _audioLooper?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _audioLooper?.TotalTime ?? TimeSpan.Zero;
        public int SampleRate => _audioLooper?.SampleRate ?? 44100;
        public float Volume { get => _audioLooper.Volume; set => _audioLooper.Volume = value; }
        public double MatchWindowSize { get => _audioLooper.MatchWindowSize; set => _audioLooper.MatchWindowSize = value; }
        public double MatchSearchRadius { get => _audioLooper.MatchSearchRadius; set => _audioLooper.MatchSearchRadius = value; }

        public event Action<MusicTrack> TrackChanged;
        public event Action<PlaybackState> StateChanged;

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
            try
            {
                var next = _playlistManager.GetNextTrack();
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
                var prev = _playlistManager.GetPreviousTrack();
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
                _playlistManager.SetNowPlayingList(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public async Task EnqueueAlbumAsync(string albumName)
        {
            var tracks = await _trackRepository.GetByAlbumAsync(albumName);
            if (tracks != null && tracks.Any())
            {
                _playlistManager.SetNowPlayingList(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public async Task EnqueuePlaylistAsync(CategoryItem playlistItem)
        {
            List<MusicTrack> tracks = null;
            if (playlistItem.Id == -1) // 全部歌曲
            {
                tracks = await _trackRepository.GetAllAsync();
            }
            else if (playlistItem.Id == -2) // 我的收藏
            {
                tracks = await _trackRepository.GetLovedTracksAsync();
            }
            else if (playlistItem.Id > 0)
            {
                tracks = await _playlistManager.GetTracksInPlaylistAsync(playlistItem.Id);
            }

            if (tracks != null && tracks.Any())
            {
                _playlistManager.SetNowPlayingList(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public void Dispose()
        {
            _audioLooper?.Dispose();
        }
    }
}
