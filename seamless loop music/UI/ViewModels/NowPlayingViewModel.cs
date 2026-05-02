using System;
using System.IO;
using System.Windows.Media.Imaging;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Commands;
using Prism.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels
{
    public class NowPlayingViewModel : BindableBase, INavigationAware
    {
        private readonly IPlaybackService _playbackService;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly TrackMetadataService _metadataService;
        private readonly seamless_loop_music.Data.Repositories.ITrackRepository _trackRepository;
        private IRegionNavigationService _navigationService;
        
        private MusicTrack _currentTrack;
        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        private BitmapImage _albumCoverImage;
        public BitmapImage AlbumCoverImage
        {
            get => _albumCoverImage;
            set => SetProperty(ref _albumCoverImage, value);
        }

        public DelegateCommand GoBackCommand { get; }
        public DelegateCommand GoToEditCommand { get; }

        public NowPlayingViewModel(IPlaybackService playbackService, IRegionManager regionManager, IEventAggregator eventAggregator, TrackMetadataService metadataService, seamless_loop_music.Data.Repositories.ITrackRepository trackRepository)
        {
            _playbackService = playbackService;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _metadataService = metadataService;
            _trackRepository = trackRepository;
            GoBackCommand = new DelegateCommand(OnGoBack);
            GoToEditCommand = new DelegateCommand(OnGoToEdit);
            
            _playbackService.TrackChanged += OnTrackChanged;
            _eventAggregator.GetEvent<seamless_loop_music.Events.TrackLoadedEvent>().Subscribe(OnTrackLoaded);
        }

        private void OnTrackLoaded(MusicTrack track)
        {
            if (track == null) return;
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentTrack = track;
                LoadAlbumCover(track);
            });
        }

        private void OnTrackChanged(MusicTrack track)
        {
            if (track == null) return;
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentTrack = track;
                LoadAlbumCover(track);
            });
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _navigationService = navigationContext.NavigationService;
            
            // 获取当前正在播放的曲目
            var track = _playbackService.CurrentTrack;
            if (track != null)
            {
                CurrentTrack = track;
                LoadAlbumCover(track);
                
                // 导航右侧列表区域
                var listParams = new NavigationParameters();
                listParams.Add("compact", true);
                listParams.Add("track", track);
                if (_playbackService.CurrentCategory != null)
                {
                    listParams.Add("category", _playbackService.CurrentCategory);
                }
                _regionManager.RequestNavigate("NowPlayingListRegion", "TrackListView", listParams);
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

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
                
                // 2. 如果路径为空，尝试从文件动态提取（兜底逻辑）
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    // 使用服务进行提取并补充 (一个专辑一个图片，GUID命名)
                    path = _metadataService.GetOrExtractCover(track);
                    
                    if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    {
                        AlbumCoverImage = null;
                        return;
                    }
                    
                    // 如果提取成功，保存曲目状态（这样下次就有了）
                    _metadataService.SaveTrack(track);

                    // 关键：触发全向修复，让同专辑的曲目以及艺术家表也瞬间获得封面
                    _trackRepository.RepairMissingCategoryCovers();

                    // 关键：通知 UI 刷新库，因为艺术家/专辑的封面可能已经同步补全了
                    _eventAggregator.GetEvent<seamless_loop_music.Events.LibraryRefreshedEvent>().Publish();
                }

                // 3. 从最终确定的路径加载
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                AlbumCoverImage = bitmap;
                return;
            }
            catch
            {
                // Ignore errors
            }
            AlbumCoverImage = null;
        }

        private void OnGoBack()
        {
            _regionManager.RequestNavigate("MainContentRegion", "LibraryView");
        }

        private void OnGoToEdit()
        {
            if (CurrentTrack != null)
            {
                var parameters = new NavigationParameters();
                parameters.Add("track", CurrentTrack);
                parameters.Add("target", "DetailView");
                parameters.Add("autoPlay", false); // 关键：不要重新加载和播放
                
                if (_playbackService.CurrentCategory != null)
                {
                    parameters.Add("category", _playbackService.CurrentCategory);
                }
                
                _regionManager.RequestNavigate("MainContentRegion", "LibraryView", parameters);
            }
        }
    }
}
