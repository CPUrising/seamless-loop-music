using System;
using System.IO;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Events;
using TagLib;

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaybackControlBarViewModel : BindableBase
    {
        private readonly IPlaybackService _playbackService;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly TrackMetadataService _metadataService;
        private readonly DispatcherTimer _statusTimer;

        private MusicTrack _currentTrack;
        public MusicTrack CurrentTrack { get => _currentTrack; set => SetProperty(ref _currentTrack, value); }

        private BitmapImage _albumCoverImage;
        public BitmapImage AlbumCoverImage
        {
            get => _albumCoverImage;
            set => SetProperty(ref _albumCoverImage, value);
        }

        private string _playState;
        public string PlayState 
        { 
            get => _playState; 
            set 
            { 
                if (SetProperty(ref _playState, value)) 
                {
                    RaisePropertyChanged(nameof(PlayButtonContent)); 
                    RaisePropertyChanged(nameof(IsPlaying));
                }
            } 
        }

        public bool IsPlaying => PlayState == "Playing";
        public string PlayButtonContent => IsPlaying ? "||" : ">";

        private string _currentTimeStr = "00:00";
        private string _totalTimeStr = "00:00";
        public string TimeDisplay => $"{_currentTimeStr} / {_totalTimeStr}";

        public string CurrentTimeStr { get => _currentTimeStr; set { if (SetProperty(ref _currentTimeStr, value)) RaisePropertyChanged(nameof(TimeDisplay)); } }
        public string TotalTimeStr { get => _totalTimeStr; set { if (SetProperty(ref _totalTimeStr, value)) RaisePropertyChanged(nameof(TimeDisplay)); } }

        private double _currentTime;
        public double CurrentTime { get => _currentTime; set => SetProperty(ref _currentTime, value); }

        private double _totalTime;
        public double TotalTime { get => _totalTime; set => SetProperty(ref _totalTime, value); }

        private double _progressValue;
        public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        public bool IsDragging { get; set; }
        public bool IsUpdating { get; set; }

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
        
        public bool IsFeatureLoopEnabled
        {
            get => _playbackService.IsFeatureLoopEnabled;
            set
            {
                if (_playbackService.IsFeatureLoopEnabled != value)
                {
                    _playbackService.IsFeatureLoopEnabled = value;
                    RaisePropertyChanged();
                }
            }
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

        public DelegateCommand PlayCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand PrevCommand { get; }
        public DelegateCommand NextCommand { get; }
        public DelegateCommand<double?> SeekCommand { get; }
        public DelegateCommand OpenDetailCommand { get; }
        public DelegateCommand ChangePlayModeCommand { get; }

        private string _playModeText;
        public string PlayModeText { get => _playModeText; set => SetProperty(ref _playModeText, value); }

        public PlaybackControlBarViewModel(IPlaybackService playbackService, IRegionManager regionManager, IEventAggregator eventAggregator, TrackMetadataService metadataService)
        {
            _playbackService = playbackService;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _metadataService = metadataService;

            PlayCommand = new DelegateCommand(OnPlayPause);
            StopCommand = new DelegateCommand(() => _playbackService.Stop());
            PrevCommand = new DelegateCommand(() => _playbackService.Previous());
            NextCommand = new DelegateCommand(() => _playbackService.Next());
            SeekCommand = new DelegateCommand<double?>(OnSeek);
            OpenDetailCommand = new DelegateCommand(OnOpenDetail);
            ChangePlayModeCommand = new DelegateCommand(OnExecuteChangePlayMode);

            UpdatePlayModeText();

            _playbackService.TrackChanged += track => 
            {
                CurrentTrack = track;
                LoadAlbumCover(track);
            };
            _playbackService.StateChanged += state => PlayState = state.ToString();
            
            _volumeValue = _playbackService.Volume * 100;

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _statusTimer.Tick += OnStatusTimerTick;
            _statusTimer.Start();
        }

        private void OnStatusTimerTick(object sender, EventArgs e)
        {
            if (_playbackService == null || IsDragging) return;

            IsUpdating = true;
            try
            {
                var pos = _playbackService.CurrentTime;
                CurrentTime = pos.TotalSeconds;
                CurrentTimeStr = pos.ToString(@"mm\:ss");

                var total = _playbackService.TotalTime;
                TotalTime = total.TotalSeconds;
                TotalTimeStr = total.ToString(@"mm\:ss");

                // 换算 0-1000 比例
                if (TotalTime > 0)
                {
                    ProgressValue = (CurrentTime / TotalTime) * 1000.0;
                }

                PlayState = _playbackService.PlaybackState.ToString();
            }
            finally
            {
                IsUpdating = false;
            }
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

        private void OnOpenDetail()
        {
            if (CurrentTrack == null) return;

            var parameters = new NavigationParameters();
            parameters.Add("track", CurrentTrack);
            parameters.Add("autoPlay", false);
            _regionManager.RequestNavigate("MainContentRegion", "NowPlayingView", parameters);
        }

        private void LoadAlbumCover(MusicTrack track)
        {
            try
            {
                if (track == null)
                {
                    AlbumCoverImage = null;
                    return;
                }

                // 1. 优先使用已缓存的路径
                string path = track.CoverPath;
                
                // 2. 如果路径为空，尝试寻找/提取 (一个专辑一个图片，GUID命名)
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    path = _metadataService.GetOrExtractCover(track);
                    if (!string.IsNullOrEmpty(path)) _metadataService.SaveTrack(track);
                }

                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 60;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    AlbumCoverImage = bitmap;
                    return;
                }
            }
            catch { }
            AlbumCoverImage = null;
        }

        public void SeekToProgress(double value)
        {
            OnSeek(value);
        }

        private void OnExecuteChangePlayMode()
        {
            var nextMode = _playbackService.PlayMode switch
            {
                PlayMode.SingleLoop => PlayMode.ListLoop,
                PlayMode.ListLoop => PlayMode.Shuffle,
                PlayMode.Shuffle => PlayMode.SingleLoop,
                _ => PlayMode.SingleLoop
            };
            _playbackService.PlayMode = nextMode;
            UpdatePlayModeText();
        }

        private void UpdatePlayModeText()
        {
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            PlayModeText = _playbackService.PlayMode switch
            {
                PlayMode.SingleLoop => isZh ? "单曲" : "Single",
                PlayMode.ListLoop => isZh ? "列表" : "List",
                PlayMode.Shuffle => isZh ? "随机" : "Shuffle",
                _ => "Single"
            };
        }
    }
}
