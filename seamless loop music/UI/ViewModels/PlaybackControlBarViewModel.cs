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
        private readonly IPlaybackService _playbackService;
        
        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set => SetProperty(ref _isDragging, value);
        }

        private bool _isUpdating;
        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
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
                    _playbackService.Volume = (float)value / 100f;
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
        public DelegateCommand PrevCommand { get; }
        public DelegateCommand NextCommand { get; }
        public DelegateCommand<double?> SeekCommand { get; }

        public PlaybackControlBarViewModel(IPlaybackService playbackService)
        {
            _playbackService = playbackService;

            PlayCommand = new DelegateCommand(ExecutePlay);
            PrevCommand = new DelegateCommand(ExecutePrev);
            NextCommand = new DelegateCommand(ExecuteNext);
            SeekCommand = new DelegateCommand<double?>(ExecuteSeek);

            _playbackService.StateChanged += OnPlayStateChanged;
            _playbackService.PositionChanged += OnPositionChanged;
            _playbackService.TrackChanged += OnTrackLoaded;
            
            // 初始化音量
            VolumeValue = _playbackService.Volume * 100;
        }

        private void OnTrackLoaded(MusicTrack track)
        {
            Application.Current?.Dispatcher?.Invoke(() => {
                // 核心修复：切歌时强制归零
                UpdateDisplay(TimeSpan.Zero, _playbackService.TotalTime);
            });
        }

        private void ExecutePlay()
        {
            if (_playbackService.PlaybackState == PlaybackState.Playing)
                _playbackService.Pause();
            else
                _playbackService.Play();
        }



        private void ExecutePrev()
        {
            // TODO: 通过 IPlaybackService 或 QueueService 实现上一首
        }

        private void ExecuteNext()
        {
            // TODO: 通过 IPlaybackService 或 QueueService 实现下一首
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
            // 核心修复：如果正在拖拽，或者底层引擎正在执行 Seek（还没填充完新数据），则无视此更新
            if (IsDragging || _playerService.IsSeeking) return;

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() => {
                // 在异步回调开始时再次确认，防止队列积压导致的旧事件生效
                if (IsDragging || _playerService.IsSeeking) return;

                UpdateDisplay(currentTime, _playerService.TotalTime);
            }));
        }

        private void ExecuteSeek(double? percentValue)
        {
            if (percentValue.HasValue)
            {
                // UI 传过来的是 0-1000 的值
                double percent = percentValue.Value / 1000.0;
                _playbackService.Seek(TimeSpan.FromSeconds(_playbackService.TotalTime.TotalSeconds * percent));
            }
        }

        private void UpdateDisplay(TimeSpan current, TimeSpan total)
        {
            IsUpdating = true; // 标记开始更新，防止 ValueChanged 误判为用户点击
            try
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
            finally
            {
                IsUpdating = false;
            }
        }

    }
}
