using System;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels
{
    public class TrayControlsViewModel : BindableBase
    {
        private readonly IPlaybackService _playbackService;
        private MusicTrack _currentTrack;
        private bool _isPlaying;

        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        private double _progressValue;
        public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        private string _currentTimeStr = "00:00";
        private string _totalTimeStr = "00:00";
        public string TimeDisplay => $"{_currentTimeStr} / {_totalTimeStr}";

        public string CurrentTimeStr { get => _currentTimeStr; set { if (SetProperty(ref _currentTimeStr, value)) RaisePropertyChanged(nameof(TimeDisplay)); } }
        public string TotalTimeStr { get => _totalTimeStr; set { if (SetProperty(ref _totalTimeStr, value)) RaisePropertyChanged(nameof(TimeDisplay)); } }

        public ICommand PlayCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PrevCommand { get; }

        public bool IsUpdating { get; set; }
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

        public TrayControlsViewModel(IPlaybackService playbackService)
        {
            _playbackService = playbackService;

            CurrentTrack = _playbackService.CurrentTrack;
            IsPlaying = _playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing;

            _playbackService.TrackChanged += track => CurrentTrack = track;
            _playbackService.StateChanged += state => IsPlaying = state == NAudio.Wave.PlaybackState.Playing;

            PlayCommand = new DelegateCommand(() => {
                if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing) _playbackService.Pause();
                else _playbackService.Play();
            });

            NextCommand = new DelegateCommand(() => _playbackService.Next());
            PrevCommand = new DelegateCommand(() => _playbackService.Previous());

            _statusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statusTimer.Tick += (s, e) => UpdateProgress();
            _statusTimer.Start();
        }

        private void UpdateProgress()
        {
            if (IsUpdating) return;
            IsUpdating = true;
            try
            {
                var pos = _playbackService.CurrentTime;
                var total = _playbackService.TotalTime;
                
                CurrentTimeStr = pos.ToString(@"mm\:ss");
                TotalTimeStr = total.ToString(@"mm\:ss");

                if (total.TotalSeconds > 0)
                {
                    ProgressValue = (pos.TotalSeconds / total.TotalSeconds) * 1000.0;
                }
            }
            finally
            {
                IsUpdating = false;
            }
        }

        public void SeekToProgress(double value)
        {
            if (_playbackService.TotalTime.TotalSeconds > 0)
            {
                var target = TimeSpan.FromSeconds(value / 1000.0 * _playbackService.TotalTime.TotalSeconds);
                _playbackService.Seek(target);
            }
        }
    }
}
