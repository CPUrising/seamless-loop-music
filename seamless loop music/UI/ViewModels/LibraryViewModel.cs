using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Events;
using System.ComponentModel;
using System.Windows.Data;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Events;
using seamless_loop_music.Data.Repositories;

namespace seamless_loop_music.UI.ViewModels
{
    public class LibraryViewModel : BindableBase, INavigationAware
    {
        private readonly ITrackRepository _trackRepository;
        private readonly IPlaybackService _playbackService;
        private readonly IPlaylistManager _playlistManager;
        private readonly IRegionManager _regionManager;
        private readonly ISearchService _searchService;
        private readonly IEventAggregator _eventAggregator;
        private string[] _currentFilterKeywords = Array.Empty<string>();
        
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

        private string _searchText;
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

        public DelegateCommand<MusicTrack> PlayCommand { get; }
        public DelegateCommand<MusicTrack> OpenDetailCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand<MusicTrack> ToggleLoveCommand { get; }
        public DelegateCommand<MusicTrack> RateCommand { get; }

        private int? _currentPlaylistId = null;
        private string _playlistName = "所有曲目";
        public string PlaylistName
        {
            get => _playlistName;
            set => SetProperty(ref _playlistName, value);
        }

        private string _playlistStats;
        public string PlaylistStats
        {
            get => _playlistStats;
            set => SetProperty(ref _playlistStats, value);
        }

        public LibraryViewModel(ITrackRepository trackRepository, IPlaybackService playbackService, IPlaylistManager playlistManager, IRegionManager regionManager, ISearchService searchService, IEventAggregator eventAggregator)
        {
            _trackRepository = trackRepository;
            _playbackService = playbackService;
            _playlistManager = playlistManager;
            _regionManager = regionManager;
            _searchService = searchService;
            _eventAggregator = eventAggregator;

            PlayCommand = new DelegateCommand<MusicTrack>(OnPlayTrack);
            OpenDetailCommand = new DelegateCommand<MusicTrack>(OnOpenDetail);
            RefreshCommand = new DelegateCommand(async () => await LoadTracksAsync(_currentPlaylistId));
            ToggleLoveCommand = new DelegateCommand<MusicTrack>(OnToggleLove);
            RateCommand = new DelegateCommand<MusicTrack>(OnRateTrack);

            // 订阅元数据变动
            _eventAggregator.GetEvent<TrackMetadataChangedEvent>().Subscribe(OnTrackMetadataChanged);
            _eventAggregator.GetEvent<PlaylistChangedEvent>().Subscribe(async () => await LoadTracksAsync(_currentPlaylistId));

            // Initialize View
            TracksView = CollectionViewSource.GetDefaultView(Tracks);
            TracksView.Filter = TracksFilter;

            // Subscribe to debounced search
            _searchService.DoSearch += (s) => App.Current.Dispatcher.Invoke(() => 
            {
                // Pre-process keywords once per search (Optimization)
                var filter = s?.Trim().ToLower();
                _currentFilterKeywords = string.IsNullOrWhiteSpace(filter) 
                    ? Array.Empty<string>() 
                    : filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                TracksView.Refresh();
            });
        }

        public async void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.ContainsKey("PlaylistId"))
            {
                _currentPlaylistId = (int)navigationContext.Parameters["PlaylistId"];
                PlaylistName = navigationContext.Parameters.ContainsKey("PlaylistName") 
                    ? (string)navigationContext.Parameters["PlaylistName"] 
                    : "未知歌单";
            }
            else
            {
                _currentPlaylistId = null;
                PlaylistName = "所有曲目";
            }

            await LoadTracksAsync(_currentPlaylistId);
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private async Task LoadTracksAsync(int? playlistId = null)
        {
            List<MusicTrack> results;
            if (playlistId == -1) // 我的最爱
            {
                results = await _trackRepository.GetLovedTracksAsync();
            }
            else if (playlistId.HasValue)
            {
                results = await _playlistManager.GetTracksInPlaylistAsync(playlistId.Value);
            }
            else
            {
                results = await _trackRepository.GetAllAsync();
            }

            App.Current.Dispatcher.Invoke(() =>
            {
                Tracks.Clear();
                foreach (var track in results)
                {
                    Tracks.Add(track);
                }
                UpdateStats();
                TracksView.Refresh();
            });
        }

        private void UpdateStats()
        {
            int count = Tracks.Count;
            // 简单的统计信息（未来可加入总时长）
            PlaylistStats = $"{count} 首曲目";
        }

        private bool TracksFilter(object item)
        {
            if (!(item is MusicTrack track)) return false;
            if (_currentFilterKeywords.Length == 0) return true;

            // Match all keywords (AND logic) - Using cached keywords for extreme speed
            return _currentFilterKeywords.All(k => 
                (track.DisplayName != null && track.DisplayName.ToLower().Contains(k)) ||
                (track.Artist != null && track.Artist.ToLower().Contains(k)) ||
                (track.Album != null && track.Album.ToLower().Contains(k)) ||
                (track.FileName != null && track.FileName.ToLower().Contains(k))
            );
        }
        
        // C# does not have a native case-insensitive "Contains" in all versions of .NET Framework, 
        // using ToLower contains is standard, but since keywords are already lowered, 
        // we only lower the track fields. 
        // Note: For absolute pro-level, we could use String.IndexOf with OrdinalIgnoreCase.

        private void OnPlayTrack(MusicTrack track)
        {
            if (track == null) return;
            
            _playlistManager.SetNowPlayingList(Tracks, track);
            _playbackService.LoadTrackAsync(track, true).ConfigureAwait(false);
        }

        private void OnOpenDetail(MusicTrack track)
        {
            if (track == null) return;
            
            var parameters = new NavigationParameters();
            parameters.Add("track", track);
            _regionManager.RequestNavigate("MainContentRegion", "DetailView", parameters);
        }

        private async void OnToggleLove(MusicTrack track)
        {
            if (track == null) return;
            track.IsLoved = !track.IsLoved;
            await _trackRepository.UpdateMetadataAsync(track.Id, track.IsLoved, track.Rating);
            _eventAggregator.GetEvent<TrackMetadataChangedEvent>().Publish(track);
            
            // 如果是在“最爱”列表中取消爱心，则需要刷新
            if (_currentPlaylistId == -1)
            {
                await LoadTracksAsync(_currentPlaylistId);
            }
        }

        private async void OnRateTrack(MusicTrack track)
        {
            if (track != null)
            {
                track.Rating = (track.Rating % 5) + 1;
                await _trackRepository.UpdateMetadataAsync(track.Id, track.IsLoved, track.Rating);
            }
        }

        private void OnTrackMetadataChanged(MusicTrack track)
        {
            // 找到本地匹配的并更新（如果是单例引用其实不需要，但为了严谨性）
            var local = Tracks.FirstOrDefault(t => t.Id == track.Id);
            if (local != null && local != track)
            {
                local.IsLoved = track.IsLoved;
                local.Rating = track.Rating;
            }
            UpdateStats();
        }
    }
}
