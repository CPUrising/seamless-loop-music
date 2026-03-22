using Prism.Commands;
using Prism.Mvvm;
using seamless_loop_music.Services;
using seamless_loop_music.Models;
using System;
using System.Windows;
using System.Windows.Threading;

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaybackControlBarViewModel : BindableBase
    {
        private readonly IPlayerService _playerService;
        private DispatcherTimer _timer;

        private string _timeDisplay = "00:00 / 00:00";
        public string TimeDisplay
        {
            get => _timeDisplay;
            set => SetProperty(ref _timeDisplay, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private double _volumeValue = 80;
        public double VolumeValue
        {
            get => _volumeValue;
            set 
            {
                if (SetProperty(ref _volumeValue, value))
                {
                    _playerService.Volume = (float)value / 100f;
                }
            }
        }

        private string _playButtonContent = "Play";
        public string PlayButtonContent
        {
            get => _playButtonContent;
            set => SetProperty(ref _playButtonContent, value);
        }

        public DelegateCommand PlayCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand PrevCommand { get; }
        public DelegateCommand NextCommand { get; }

        public PlaybackControlBarViewModel(IPlayerService playerService)
        {
            _playerService = playerService;

            PlayCommand = new DelegateCommand(ExecutePlay);
            StopCommand = new DelegateCommand(ExecuteStop);
            PrevCommand = new DelegateCommand(ExecutePrev);
            NextCommand = new DelegateCommand(ExecuteNext);

            _playerService.OnPlayStateChanged += OnPlayStateChanged;
            
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) => UpdateProgress();
            _timer.Start();

            // 初始化音量
            VolumeValue = _playerService.Volume * 100;
        }

        private void ExecutePlay()
        {
            if (_playerService.PlaybackState == PlaybackState.Playing)
                _playerService.Pause();
            else
                _playerService.Play();
        }

        private void ExecuteStop()
        {
            _playerService.Stop();
        }

        private void ExecutePrev()
        {
            _playerService.Previous();
        }

        private void ExecuteNext()
        {
            _playerService.Next();
        }

        private void OnPlayStateChanged(PlaybackState state)
        {
            PlayButtonContent = state == PlaybackState.Playing ? 
                LocalizationService.Instance["Pause"] : LocalizationService.Instance["Play"];
        }

        private void UpdateProgress()
        {
            if (_playerService.TotalTime.TotalSeconds > 0)
            {
                ProgressValue = (_playerService.CurrentTime.TotalSeconds / _playerService.TotalTime.TotalSeconds) * 1000;
                TimeDisplay = $"{_playerService.CurrentTime:mm\\:ss} / {_playerService.TotalTime:mm\\:ss}";
            }
            else
            {
                ProgressValue = 0;
                TimeDisplay = "00:00 / 00:00";
            }
        }
    }
}
