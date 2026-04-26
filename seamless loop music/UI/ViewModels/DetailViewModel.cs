using System;
using System.IO;
using System.Windows.Media.Imaging;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Commands;
using Prism.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using TagLib;

namespace seamless_loop_music.UI.ViewModels
{
    public class DetailViewModel : BindableBase, INavigationAware
    {
        private readonly IPlaybackService _playbackService;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private IRegionNavigationService _navigationService;
        
        private MusicTrack _currentTrack;
        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        private string _albumInfo;
        public string AlbumInfo
        {
            get => _albumInfo;
            set => SetProperty(ref _albumInfo, value);
        }

        public DelegateCommand GoBackCommand { get; }
        public DelegateCommand GoToDetailCommand { get; }

        public DetailViewModel(IPlaybackService playbackService, IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _playbackService = playbackService;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            GoBackCommand = new DelegateCommand(OnGoBack);
            GoToDetailCommand = new DelegateCommand(OnGoToDetail);
            _playbackService.TrackChanged += OnTrackChanged;
        }

        private void OnTrackChanged(MusicTrack track)
        {
            if (track == null) return;
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentTrack = track;
                UpdateAlbumInfo(track);
            });
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 保存导航服务用于返回
            _navigationService = navigationContext.NavigationService;
            
            if (navigationContext.Parameters.ContainsKey("track"))
            {
                var track = navigationContext.Parameters["track"] as MusicTrack;
                bool autoPlay = true;
                if (navigationContext.Parameters.ContainsKey("autoPlay"))
                {
                    autoPlay = (bool)navigationContext.Parameters["autoPlay"];
                }
                
                CategoryItem category = null;
                if (navigationContext.Parameters.ContainsKey("category"))
                {
                    category = navigationContext.Parameters["category"] as CategoryItem;
                }
                
                if (track != null)
                {
                    CurrentTrack = track;
                    UpdateAlbumInfo(track);
                    
                    // 1. 将歌曲列表导航到侧边栏区域，透传分类上下文
                    var listParams = new NavigationParameters();
                    listParams.Add("compact", true);
                    listParams.Add("track", track);
                    if (category != null) listParams.Add("category", category);
                    _regionManager.RequestNavigate("DetailListRegion", "TrackListView", listParams);

                    // 2. 重要：告诉 LoopWorkspace 显示该曲目的编辑数据，但不影响当前播放
                    _eventAggregator.GetEvent<seamless_loop_music.Events.TrackLoadedEvent>().Publish(track);
                }
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }


        private void UpdateAlbumInfo(MusicTrack track)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(track.Artist)) parts.Add(track.Artist);
            if (!string.IsNullOrEmpty(track.Album)) parts.Add(track.Album);
            AlbumInfo = parts.Count > 0 ? string.Join(" - ", parts) : "未知专辑";
        }

        private void OnGoBack()
        {
            if (_navigationService?.Journal.CanGoBack == true)
            {
                _navigationService.Journal.GoBack();
            }
            else
            {
                _regionManager.RequestNavigate("LibraryContentRegion", "TrackListView");
            }
        }

        private void OnGoToDetail()
        {
            _regionManager.RequestNavigate("MainContentRegion", "NowPlayingView");
        }
    }
}
