using NAudio.Wave;
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
        
        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set => SetProperty(ref _isDragging, value);
        }

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
        public DelegateCommand<double?> SeekCommand { get; }

        public PlaybackControlBarViewModel(IPlayerService playerService)
        {
            _playerService = playerService;

            PlayCommand = new DelegateCommand(ExecutePlay);
            StopCommand = new DelegateCommand(ExecuteStop);
            PrevCommand = new DelegateCommand(ExecutePrev);
            NextCommand = new DelegateCommand(ExecuteNext);
            SeekCommand = new DelegateCommand<double?>(ExecuteSeek);

            _playerService.OnPlayStateChanged += OnPlayStateChanged;
            _playerService.OnPositionChanged += OnPositionChanged;
            
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
            Application.Current?.Dispatcher?.Invoke(() => {
                PlayButtonContent = state == PlaybackState.Playing ? 
                    LocalizationService.Instance["Pause"] : LocalizationService.Instance["Play"];
            });
        }

        private void OnPositionChanged(TimeSpan currentTime)
        {
            if (IsDragging) return;

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() => {
                UpdateDisplay(currentTime, _playerService.TotalTime);
            }));
        }

        private void ExecuteSeek(double? percentValue)
        {
            if (percentValue.HasValue)
            {
                // UI 传过来的是 0-1000 的值
                _playerService.Seek(percentValue.Value / 1000.0);
            }
        }

        private void UpdateDisplay(TimeSpan current, TimeSpan total)
        {
            if (total.TotalSeconds > 0)
            {
                ProgressValue = (current.TotalSeconds / total.TotalSeconds) * 1000;
                TimeDisplay = $"{current:mm\\:ss} / {total:mm\\:ss}";
            }
            else
            {
                ProgressValue = 0;
                TimeDisplay = "00:00 / 00:00";
            }
        }

        // 兼容性保留，但逻辑已拆分
        private void UpdateProgress() 
        {
             UpdateDisplay(_playerService.CurrentTime, _playerService.TotalTime);
        }
    }
}
