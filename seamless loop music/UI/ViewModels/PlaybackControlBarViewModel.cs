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
        public string PlayState { get => _playState; set { if (SetProperty(ref _playState, value)) RaisePropertyChanged(nameof(PlayButtonContent)); } }

        public string PlayButtonContent => PlayState == "Playing" ? "||" : ">";

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

        public DelegateCommand PlayCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand PrevCommand { get; }
        public DelegateCommand NextCommand { get; }
        public DelegateCommand<double?> SeekCommand { get; }
        public DelegateCommand OpenDetailCommand { get; }

        public PlaybackControlBarViewModel(IPlaybackService playbackService, IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _playbackService = playbackService;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;

            PlayCommand = new DelegateCommand(OnPlayPause);
            StopCommand = new DelegateCommand(() => _playbackService.Stop());
            PrevCommand = new DelegateCommand(() => _playbackService.Previous());
            NextCommand = new DelegateCommand(() => _playbackService.Next());
            SeekCommand = new DelegateCommand<double?>(OnSeek);
            OpenDetailCommand = new DelegateCommand(OnOpenDetail);

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
            _regionManager.RequestNavigate("MainContentRegion", "DetailView", parameters);
        }

        private void LoadAlbumCover(MusicTrack track)
        {
            try
            {
                if (track == null || !System.IO.File.Exists(track.FilePath))
                {
                    AlbumCoverImage = null;
                    return;
                }

                using (var file = TagLib.File.Create(track.FilePath))
                {
                    if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                    {
                        var picture = file.Tag.Pictures[0];
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = new MemoryStream(picture.Data.Data);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 60;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        AlbumCoverImage = bitmap;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[封面加载失败] {ex.Message}");
            }
            AlbumCoverImage = null;
        }
    }
}
