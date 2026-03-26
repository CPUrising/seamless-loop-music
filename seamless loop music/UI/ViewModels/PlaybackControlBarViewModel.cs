using System;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Events;

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaybackControlBarViewModel : BindableBase
    {
        private readonly IPlaybackService _playbackService;
        private readonly IEventAggregator _eventAggregator;

        private MusicTrack _currentTrack;
        public MusicTrack CurrentTrack { get => _currentTrack; set => SetProperty(ref _currentTrack, value); }

        private string _playState;
        public string PlayState { get => _playState; set { if (SetProperty(ref _playState, value)) RaisePropertyChanged(nameof(PlayButtonContent)); } }

        public string PlayButtonContent => PlayState == "Playing" ? "||" : ">";

        private string _currentTimeStr = "00:00";
        public string TimeDisplay => $"{_currentTimeStr} / {_totalTimeStr}";

        private string _totalTimeStr = "00:00";
        public string CurrentTimeStr { get => _currentTimeStr; set { if (SetProperty(ref _currentTimeStr, value)) RaisePropertyChanged(nameof(TimeDisplay)); } }
        public string TotalTimeStr { get => _totalTimeStr; set { if (SetProperty(ref _totalTimeStr, value)) RaisePropertyChanged(nameof(TimeDisplay)); } }

        private double _progressValue;
        public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        private double _volumeValue = 100;
        public double VolumeValue 
        { 
            get => _volumeValue; 
            set 
            { 
                if (SetProperty(ref _volumeValue, value)) 
                    _playbackService.Volume = (float)(value / 100.0); 
            } 
        }

        private bool _isDragging;
        public bool IsDragging { get => _isDragging; set => SetProperty(ref _isDragging, value); }

        private bool _isUpdating;
        public bool IsUpdating { get => _isUpdating; set => SetProperty(ref _isUpdating, value); }

        public DelegateCommand PlayCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand PrevCommand { get; }
        public DelegateCommand NextCommand { get; }
        public DelegateCommand<double?> SeekCommand { get; }

        public PlaybackControlBarViewModel(IPlaybackService playbackService, IEventAggregator eventAggregator)
        {
            _playbackService = playbackService;
            _eventAggregator = eventAggregator;

            PlayCommand = new DelegateCommand(OnPlayPause);
            StopCommand = new DelegateCommand(() => _playbackService.Stop());
            PrevCommand = new DelegateCommand(() => _playbackService.Previous());
            NextCommand = new DelegateCommand(() => _playbackService.Next());
            SeekCommand = new DelegateCommand<double?>(OnSeek);

            _playbackService.TrackChanged += track => CurrentTrack = track;
            _playbackService.StateChanged += state => PlayState = state.ToString();
            _playbackService.PositionChanged += OnPositionChanged;
            
            _volumeValue = _playbackService.Volume * 100;
        }

        private void OnPlayPause()
        {
            if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing) _playbackService.Pause();
            else _playbackService.Play();
        }

        private void OnSeek(double? value)
        {
            if (value.HasValue && _playbackService.TotalTime.TotalSeconds > 0)
            {
                var target = TimeSpan.FromSeconds(value.Value / 1000.0 * _playbackService.TotalTime.TotalSeconds);
                _playbackService.Seek(target);
            }
        }

        private void OnPositionChanged(TimeSpan position)
        {
            if (IsDragging) return;
            IsUpdating = true;
            CurrentTimeStr = position.ToString(@"mm\:ss");
            var total = _playbackService.TotalTime;
            TotalTimeStr = total.ToString(@"mm\:ss");
            if (total.TotalSeconds > 0) ProgressValue = position.TotalSeconds / total.TotalSeconds * 1000;
            IsUpdating = false;
        }
    }
}
