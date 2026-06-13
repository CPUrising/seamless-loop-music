using System;
using System.Windows;
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
        private readonly IAppStateService _appStateService;
        private MusicTrack _currentTrack;
        private bool _isPlaying;
        private bool _isPlayModeMenuOpen;

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
        public ICommand ToggleMainWindowCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand TogglePlayModeMenuCommand { get; }

        public string ShowHideText => IsMainWindowVisible ? LocalizationService.Instance["MenuHideToTray"] : LocalizationService.Instance["MenuShowMainWindow"];
        public string PlayPauseText => IsPlaying ? LocalizationService.Instance["MenuPause"] : LocalizationService.Instance["MenuPlay"];
        public string PreviousText => LocalizationService.Instance["MenuPrevious"];
        public string NextText => LocalizationService.Instance["MenuNext"];
        public string ExitText => LocalizationService.Instance["MenuExit"];
        public string PlayModeText => LocalizationService.Instance["TrayPlayMode"];
        public string SeamlessLoopText => LocalizationService.Instance["FeatureLoop"];
        public string SingleLoopText => LocalizationService.Instance["TipPlayModeSingle"];
        public string ListLoopText => LocalizationService.Instance["TipPlayModeList"];
        public string ShuffleText => LocalizationService.Instance["TipPlayModeShuffle"];

        public bool IsMainWindowVisible => Application.Current?.MainWindow?.IsVisible ?? false;

        public bool IsPlayModeMenuOpen
        {
            get => _isPlayModeMenuOpen;
            set => SetProperty(ref _isPlayModeMenuOpen, value);
        }

        public bool IsSeamlessLoopEnabled
        {
            get => _playbackService.IsSeamlessLoopEnabled;
            set
            {
                if (_playbackService.IsSeamlessLoopEnabled != value)
                {
                    _playbackService.IsSeamlessLoopEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsSingleLoop
        {
            get => _playbackService.PlayMode == PlayMode.SingleLoop;
            set
            {
                if (!value) return;

                if (_playbackService.PlayMode != PlayMode.SingleLoop)
                {
                    _playbackService.PlayMode = PlayMode.SingleLoop;
                }

                IsPlayModeMenuOpen = false;
            }
        }

        public bool IsListLoop
        {
            get => _playbackService.PlayMode == PlayMode.ListLoop;
            set
            {
                if (!value) return;

                if (_playbackService.PlayMode != PlayMode.ListLoop)
                {
                    _playbackService.PlayMode = PlayMode.ListLoop;
                }

                IsPlayModeMenuOpen = false;
            }
        }

        public bool IsShuffle
        {
            get => _playbackService.PlayMode == PlayMode.Shuffle;
            set
            {
                if (!value) return;

                if (_playbackService.PlayMode != PlayMode.Shuffle)
                {
                    _playbackService.PlayMode = PlayMode.Shuffle;
                }

                IsPlayModeMenuOpen = false;
            }
        }

        public bool IsUpdating { get; set; }
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

        public TrayControlsViewModel(IPlaybackService playbackService, IAppStateService appStateService)
        {
            _playbackService = playbackService;
            _appStateService = appStateService;

            CurrentTrack = _playbackService.CurrentTrack;
            IsPlaying = _playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing;

            _playbackService.TrackChanged += track => CurrentTrack = track;
            _playbackService.StateChanged += state =>
            {
                IsPlaying = state == NAudio.Wave.PlaybackState.Playing;
                RaisePropertyChanged(nameof(PlayPauseText));
            };
            _playbackService.SeamlessLoopChanged += _ => RaisePropertyChanged(nameof(IsSeamlessLoopEnabled));
            _playbackService.PlayModeChanged += _ => RaisePlayModeChanged();

            PlayCommand = new DelegateCommand(() => {
                if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing) _playbackService.Pause();
                else _playbackService.Play();
            });

            NextCommand = new DelegateCommand(() => _playbackService.Next());
            PrevCommand = new DelegateCommand(() => _playbackService.Previous());
            ToggleMainWindowCommand = new DelegateCommand(ToggleMainWindow);
            TogglePlayModeMenuCommand = new DelegateCommand(() => IsPlayModeMenuOpen = !IsPlayModeMenuOpen);
            ExitCommand = new DelegateCommand(() =>
            {
                _appStateService.IsExiting = true;
                Application.Current.Shutdown();
            });

            _statusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statusTimer.Tick += (s, e) => UpdateProgress();
            _statusTimer.Start();
        }

        public void RefreshMenuState()
        {
            IsPlayModeMenuOpen = false;
            RaisePropertyChanged(nameof(IsMainWindowVisible));
            RaisePropertyChanged(nameof(ShowHideText));
            RaisePropertyChanged(nameof(PlayPauseText));
            RaisePropertyChanged(nameof(IsSeamlessLoopEnabled));
            RaisePlayModeChanged();
            RaisePropertyChanged(nameof(PlayModeText));
            RaisePropertyChanged(nameof(SeamlessLoopText));
            RaisePropertyChanged(nameof(SingleLoopText));
            RaisePropertyChanged(nameof(ListLoopText));
            RaisePropertyChanged(nameof(ShuffleText));
        }

        private void RaisePlayModeChanged()
        {
            RaisePropertyChanged(nameof(IsSingleLoop));
            RaisePropertyChanged(nameof(IsListLoop));
            RaisePropertyChanged(nameof(IsShuffle));
        }

        private void ToggleMainWindow()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            if (mainWindow.IsVisible)
            {
                mainWindow.Hide();
            }
            else
            {
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;

                mainWindow.Show();
                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
            }

            RefreshMenuState();
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
