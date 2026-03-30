using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.ComponentModel;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Events;
using Prism.Regions;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Events;
using seamless_loop_music.Data.Repositories;

namespace seamless_loop_music.UI.ViewModels
{
    public class TrackListViewModel : BindableBase, INavigationAware
    {
        private readonly ITrackRepository _trackRepository;
        private readonly IPlaybackService _playbackService;
        private readonly IPlaylistManager _playlistManager;
        private readonly ISearchService _searchService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRegionManager _regionManager;

        private string[] _currentFilterKeywords = Array.Empty<string>();
        private CategoryItem _selectedCategoryItem;
        private HashSet<int> _currentPlaylistTrackIds = new HashSet<int>();

        private ObservableCollection<MusicTrack> _tracks = new ObservableCollection<MusicTrack>();
        public ObservableCollection<MusicTrack> Tracks
        {
            get => _tracks;
            set => SetProperty(ref _tracks, value);
        }

        private ICollectionView _tracksView;
        public ICollectionView TracksView
        {
            get => _tracksView;
            set => SetProperty(ref _tracksView, value);
        }

        public string SearchText
        {
            get => _searchService.SearchText;
            set 
            {
                if (_searchService.SearchText != value)
                {
                    _searchService.SearchText = value;
                    RaisePropertyChanged(nameof(SearchText));
                }
            }
        }

        private string _searchPlaceholder = "在库中搜索...";
        public string SearchPlaceholder
        {
            get => _searchPlaceholder;
            set => SetProperty(ref _searchPlaceholder, value);
        }

        private string _playlistStats;
        public string PlaylistStats
        {
            get => _playlistStats;
            set => SetProperty(ref _playlistStats, value);
        }

        public DelegateCommand<MusicTrack> PlayCommand { get; }
        public DelegateCommand<MusicTrack> OpenDetailCommand { get; }
        public DelegateCommand<MusicTrack> ToggleLoveCommand { get; }
        public DelegateCommand<MusicTrack> RateCommand { get; }

        public TrackListViewModel(
            ITrackRepository trackRepository, 
            IPlaybackService playbackService, 
            IPlaylistManager playlistManager, 
            ISearchService searchService, 
            IEventAggregator eventAggregator,
            IRegionManager regionManager)
        {
            _trackRepository = trackRepository;
            _playbackService = playbackService;
            _playlistManager = playlistManager;
            _searchService = searchService;
            _eventAggregator = eventAggregator;
            _regionManager = regionManager;

            PlayCommand = new DelegateCommand<MusicTrack>(OnPlayTrack);
            OpenDetailCommand = new DelegateCommand<MusicTrack>(OnOpenDetail);
            ToggleLoveCommand = new DelegateCommand<MusicTrack>(OnToggleLove);
            RateCommand = new DelegateCommand<MusicTrack>(OnRateTrack);

            // 初始化视图
            TracksView = CollectionViewSource.GetDefaultView(Tracks);
            TracksView.Filter = TracksFilter;

            // 监听分类选中事件
            _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Subscribe(OnCategoryItemSelected);
            
            // 监听元数据变更
            _eventAggregator.GetEvent<TrackMetadataChangedEvent>().Subscribe(OnTrackMetadataChanged);

            // 搜索逻辑
            _searchService.DoSearch += (s) => App.Current.Dispatcher.Invoke(() => 
            {
                var filter = s?.Trim().ToLower();
                _currentFilterKeywords = string.IsNullOrWhiteSpace(filter) 
                    ? Array.Empty<string>() 
                    : filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                TracksView.Refresh();
            });
        }

        private async void OnCategoryItemSelected(CategoryItem item)
        {
            _selectedCategoryItem = item;
            
            // 如果是常规歌单，需要加载其包含的曲目 ID
            if (item != null && item.Type == CategoryType.Playlist && item.Id > 0)
            {
                var tracks = await _playlistManager.GetTracksInPlaylistAsync(item.Id);
                _currentPlaylistTrackIds = new HashSet<int>(tracks.Select(t => t.Id));
            }
            else
            {
                _currentPlaylistTrackIds.Clear();
            }

            UpdateSearchPlaceholder();
            TracksView.Refresh();
            UpdateStats();
        }

        private void UpdateSearchPlaceholder()
        {
            SearchPlaceholder = _selectedCategoryItem == null 
                ? "在库中搜索..." 
                : $"在 {_selectedCategoryItem.Name} 中搜索...";
        }

        private void UpdateStats()
        {
            var count = TracksView.Cast<object>().Count();
            PlaylistStats = $"{count} 首曲目";
        }

        private bool TracksFilter(object item)
        {
            if (!(item is MusicTrack track)) return false;

            if (_selectedCategoryItem != null)
            {
                switch (_selectedCategoryItem.Type)
                {
                    case CategoryType.Album:
                        if (track.Album != _selectedCategoryItem.Name) return false;
                        break;
                    case CategoryType.Artist:
                        if (track.Artist != _selectedCategoryItem.Name) return false;
                        break;
                    case CategoryType.Playlist:
                        if (_selectedCategoryItem.Id == -1) // 全部歌曲
                        {
                            // 不进行任何分类过滤
                        }
                        else if (_selectedCategoryItem.Id == -2) // 我的收藏
                        {
                            if (!track.IsLoved) return false;
                        }
                        else if (_selectedCategoryItem.Id > 0) // 普通歌单
                        {
                            if (!_currentPlaylistTrackIds.Contains(track.Id)) return false;
                        }
                        break;
                }
            }

            if (_currentFilterKeywords.Length == 0) return true;

            return _currentFilterKeywords.All(k => 
                (track.DisplayName != null && track.DisplayName.ToLower().Contains(k)) ||
                (track.Artist != null && track.Artist.ToLower().Contains(k)) ||
                (track.Album != null && track.Album.ToLower().Contains(k)) ||
                (track.FileName != null && track.FileName.ToLower().Contains(k))
            );
        }

        private void OnPlayTrack(MusicTrack track)
        {
            if (track == null) return;
            _playlistManager.SetNowPlayingList(Tracks, track);
            _playbackService.LoadTrackAsync(track, true).ConfigureAwait(false);
        }

        private void OnOpenDetail(MusicTrack track)
        {
            if (track == null) return;
            
            // 重要：在当前区域内进行导航
            var parameters = new NavigationParameters();
            parameters.Add("track", track);
            parameters.Add("autoPlay", false);
            _regionManager.RequestNavigate("LibraryContentRegion", "DetailView", parameters);
        }

        private async void OnToggleLove(MusicTrack track)
        {
            if (track == null) return;
            track.IsLoved = !track.IsLoved;
            await _trackRepository.UpdateMetadataAsync(track.Id, track.IsLoved, track.Rating);
            _eventAggregator.GetEvent<TrackMetadataChangedEvent>().Publish(track);
        }

        private async void OnRateTrack(MusicTrack track)
        {
            if (track == null) return;
            track.Rating = (track.Rating + 1) % 6;
            await _trackRepository.UpdateMetadataAsync(track.Id, track.IsLoved, track.Rating);
        }

        private void OnTrackMetadataChanged(MusicTrack track)
        {
            var local = Tracks.FirstOrDefault(t => t.Id == track.Id);
            if (local != null && local != track)
            {
                local.IsLoved = track.IsLoved;
                local.Rating = track.Rating;
            }
        }

        public async void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 加载全量数据
            var results = await _trackRepository.GetAllAsync();
            App.Current.Dispatcher.Invoke(() =>
            {
                Tracks.Clear();
                foreach (var t in results) Tracks.Add(t);
                UpdateStats();
                TracksView.Refresh();
            });
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
