using System;
using System.Windows.Threading;
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
        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        private string _playState;
        public string PlayState
        {
            get => _playState;
            set => SetProperty(ref _playState, value);
        }

        private string _currentTimeStr = "00:00";
        public string CurrentTimeStr
        {
            get => _currentTimeStr;
            set => SetProperty(ref _currentTimeStr, value);
        }

        private string _totalTimeStr = "00:00";
        public string TotalTimeStr
        {
            get => _totalTimeStr;
            set => SetProperty(ref _totalTimeStr, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public DelegateCommand PlayPauseCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand PreviousCommand { get; }
        public DelegateCommand NextCommand { get; }

        public PlaybackControlBarViewModel(IPlaybackService playbackService, IEventAggregator eventAggregator)
        {
            _playbackService = playbackService;
            _eventAggregator = eventAggregator;

            PlayPauseCommand = new DelegateCommand(OnPlayPause);
            StopCommand = new DelegateCommand(() => _playbackService.Stop());
            PreviousCommand = new DelegateCommand(() => { /* TODO */ });
            NextCommand = new DelegateCommand(() => { /* TODO */ });

            _playbackService.TrackChanged += track => CurrentTrack = track;
            _playbackService.StateChanged += state => PlayState = state.ToString();
            _playbackService.PositionChanged += OnPositionChanged;

            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(track => CurrentTrack = track);
        }

        private void OnPlayPause()
        {
            if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                _playbackService.Pause();
            else
                _playbackService.Play();
        }

        private void OnPositionChanged(TimeSpan position)
        {
            CurrentTimeStr = position.ToString(@"mm\:ss");
            var total = _playbackService.TotalTime;
            TotalTimeStr = total.ToString(@"mm\:ss");

            if (total.TotalSeconds > 0)
                Progress = position.TotalSeconds / total.TotalSeconds * 100;
        }
    }
}
