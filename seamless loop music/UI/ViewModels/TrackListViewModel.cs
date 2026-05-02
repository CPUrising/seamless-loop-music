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
using seamless_loop_music.UI.Views;

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
        private readonly IPlayerService _playerService;

        private string[] _currentFilterKeywords = Array.Empty<string>();
        private CategoryItem _selectedCategoryItem;
        public CategoryItem SelectedCategoryItem
        {
            get => _selectedCategoryItem;
            set => SetProperty(ref _selectedCategoryItem, value);
        }
        private HashSet<int> _currentPlaylistTrackIds = new HashSet<int>();

        private ObservableCollection<MusicTrack> _tracks = new ObservableCollection<MusicTrack>();
        public ObservableCollection<MusicTrack> Tracks
        {
            get => _tracks;
            set => SetProperty(ref _tracks, value);
        }

        private ObservableCollection<MusicTrack> _selectedTracks = new ObservableCollection<MusicTrack>();
        public ObservableCollection<MusicTrack> SelectedTracks
        {
            get => _selectedTracks;
            set => SetProperty(ref _selectedTracks, value);
        }

        private ICollectionView _tracksView;
        public ICollectionView TracksView
        {
            get => _tracksView;
            set => SetProperty(ref _tracksView, value);
        }

        private bool _isCompact;
        public bool IsCompact
        {
            get => _isCompact;
            set => SetProperty(ref _isCompact, value);
        }

        private int _playingTrackId;
        public int PlayingTrackId
        {
            get => _playingTrackId;
            set 
            {
                if (SetProperty(ref _playingTrackId, value))
                {
                    UpdatePlayingStatus();
                }
            }
        }

        private MusicTrack _selectedTrack;
        public MusicTrack SelectedTrack
        {
            get => _selectedTrack;
            set 
            {
                if (SetProperty(ref _selectedTrack, value) && value != null && IsCompact)
                {
                    // 在精简模式下，点击曲目即视为选择编辑该曲目
                    OnOpenDetail(value);
                }
            }
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

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => App.Current.Dispatcher.Invoke(() => SetProperty(ref _statusMessage, value));
        }

        private bool _isAnalyzing;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set => SetProperty(ref _isAnalyzing, value);
        }

        public DelegateCommand<MusicTrack> PlayCommand { get; }
        public DelegateCommand<MusicTrack> OpenDetailCommand { get; }
        public DelegateCommand<MusicTrack> RateCommand { get; }
        public DelegateCommand<MusicTrack> AnalyzeTrackCommand { get; }

        public DelegateCommand<MusicTrack> ShowInExplorerCommand { get; }
        public DelegateCommand<MusicTrack> AddToPlaylistCommand { get; }
        public DelegateCommand<MusicTrack> RemoveFromListCommand { get; }
        public DelegateCommand<MusicTrack> DeleteFromDiskCommand { get; }
        public DelegateCommand<MusicTrack> RenameAliasCommand { get; }
        public DelegateCommand PlaySelectedCommand { get; }
        public DelegateCommand BatchAnalyzeCommand { get; }
        public DelegateCommand SelectAllCommand { get; }

        public TrackListViewModel(
            ITrackRepository trackRepository, 
            IPlaybackService playbackService, 
            IPlaylistManager playlistManager, 
            ISearchService searchService, 
            IEventAggregator eventAggregator,
            IRegionManager regionManager,
            IPlayerService playerService)
        {
            _trackRepository = trackRepository;
            _playbackService = playbackService;
            _playlistManager = playlistManager;
            _searchService = searchService;
            _eventAggregator = eventAggregator;
            _regionManager = regionManager;
            _playerService = playerService;

            PlayCommand = new DelegateCommand<MusicTrack>(OnPlayTrack);
            OpenDetailCommand = new DelegateCommand<MusicTrack>(OnOpenDetail);
            RateCommand = new DelegateCommand<MusicTrack>(OnRateTrack);
            AnalyzeTrackCommand = new DelegateCommand<MusicTrack>(OnAnalyzeTrack);

            ShowInExplorerCommand = new DelegateCommand<MusicTrack>(OnShowInExplorer);
            AddToPlaylistCommand = new DelegateCommand<MusicTrack>(OnAddToPlaylist);
            RemoveFromListCommand = new DelegateCommand<MusicTrack>(OnRemoveFromList);
            DeleteFromDiskCommand = new DelegateCommand<MusicTrack>(OnDeleteFromDisk);
            RenameAliasCommand = new DelegateCommand<MusicTrack>(OnRenameAlias);
            PlaySelectedCommand = new DelegateCommand(OnPlaySelected);
            BatchAnalyzeCommand = new DelegateCommand(OnBatchAnalyze);
            SelectAllCommand = new DelegateCommand(OnSelectAll);

            // 初始化视图
            TracksView = CollectionViewSource.GetDefaultView(Tracks);
            TracksView.Filter = TracksFilter;

            // 监听分类选中事件
            _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Subscribe(OnCategoryItemSelected);
            
            // 监听元数据变更
            _eventAggregator.GetEvent<TrackMetadataChangedEvent>().Subscribe(OnTrackMetadataChanged);

            // 监听音乐库刷新（扫描完成后重载数据）
            _eventAggregator.GetEvent<LibraryRefreshedEvent>().Subscribe(async () => await ReloadTracksAsync());

            // 搜索逻辑
            _searchService.DoSearch += (s) => App.Current.Dispatcher.Invoke(() => 
            {
                var filter = s?.Trim().ToLower();
                _currentFilterKeywords = string.IsNullOrWhiteSpace(filter) 
                    ? Array.Empty<string>() 
                    : filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                TracksView.Refresh();
            });

            // 监听全局状态消息
            _eventAggregator.GetEvent<StatusMessageEvent>().Subscribe(msg => 
            {
                App.Current.Dispatcher.Invoke(() => StatusMessage = msg);
            });

            // 监听语言切换事件
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(c => 
            {
                UpdateSearchPlaceholder();
                UpdateStats();
                RaisePropertyChanged(nameof(CategoryName));
            });

            // 监听播放曲目变更
            _playbackService.TrackChanged += (track) => 
            {
                App.Current.Dispatcher.Invoke(() => 
                {
                    PlayingTrackId = track?.Id ?? 0;
                    if (track != null)
                    {
                        var playingTrack = Tracks.FirstOrDefault(t => t.Id == track.Id);
                        if (playingTrack != null)
                        {
                            _eventAggregator.GetEvent<ScrollToTrackEvent>().Publish(playingTrack);
                        }
                    }
                });
            };

            // 初始化当前播放曲目 ID
            if (_playbackService.CurrentTrack != null)
            {
                PlayingTrackId = _playbackService.CurrentTrack.Id;
            }

            UpdateSearchPlaceholder();
        }

        public string CategoryName => _selectedCategoryItem?.Name ?? LocalizationService.Instance["PlaylistAll"];

        private void UpdatePlayingStatus()
        {
            foreach (var track in Tracks)
            {
                track.IsPlaying = track.Id == _playingTrackId;
            }
        }

        private async void OnCategoryItemSelected(CategoryItem item)
        {
            await ApplyCategoryFilterAsync(item);
        }

        private async Task ApplyCategoryFilterAsync(CategoryItem item)
        {
            // 深度修复：先准备好过滤数据，再切换分类状态，防止竞态条件下过滤器看到空的 ID 集合
            HashSet<int> newPlaylistIds = new HashSet<int>();

            // 如果是常规歌单，需要加载其包含的曲目 ID
            if (item != null && item.Type == CategoryType.Playlist && item.Id > 0)
            {
                var tracks = await _playlistManager.GetTracksInPlaylistAsync(item.Id);
                newPlaylistIds = new HashSet<int>(tracks.Select(t => t.Id));
            }
            else if (item != null && item.Type == CategoryType.Playlist && item.Id == -2)
            {
                // “我的收藏”特殊处理已经在 TracksFilter 里了，这里只需要确保集合不干扰
                newPlaylistIds.Clear();
            }

            // 更新状态
            _currentPlaylistTrackIds = newPlaylistIds;
            _selectedCategoryItem = item;
            
            App.Current.Dispatcher.Invoke(() => 
            {
                UpdateSearchPlaceholder();

                // 只有在切换到“Rating歌单”时才应用星级排序
                TracksView.SortDescriptions.Clear();
                if (_selectedCategoryItem?.Id == -2)
                {
                    TracksView.SortDescriptions.Add(new SortDescription("Rating", ListSortDirection.Descending));
                    TracksView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                }

                TracksView.Refresh();
                UpdateStats();
                RaisePropertyChanged(nameof(CategoryName));
            });
        }

        private void UpdateSearchPlaceholder()
        {
            var loc = LocalizationService.Instance;
            SearchPlaceholder = _selectedCategoryItem == null 
                ? loc["SearchPlaceholder"] 
                : string.Format(loc["SearchInPlaceholder"], _selectedCategoryItem.Name);
        }

        private void UpdateStats()
        {
            var count = TracksView.Cast<object>().Count();
            PlaylistStats = string.Format(LocalizationService.Instance["PlaylistStatsFormat"], count);
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
                        else if (_selectedCategoryItem.Id == -2) // Rating歌单
                        {
                            if (track.Rating <= 0) return false;
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
            _playbackService.SetQueue(TracksView.Cast<MusicTrack>().ToList(), track);
            _playbackService.LoadTrackAsync(track, true);
        }

        private void OnOpenDetail(MusicTrack track)
        {
            if (track == null) return;
            
            if (IsCompact)
            {
                // 如果是精简模式（侧边栏），则直接发送加载事件给 DetailViewModel 和 LoopWorkspace
                _eventAggregator.GetEvent<seamless_loop_music.Events.TrackLoadedEvent>().Publish(track);
                return;
            }

            // 重要：在当前区域内进行导航
            var parameters = new NavigationParameters();
            parameters.Add("track", track);
            parameters.Add("category", _selectedCategoryItem); // 携带当前分类上下文
            parameters.Add("autoPlay", false);
            _regionManager.RequestNavigate("LibraryContentRegion", "DetailView", parameters);
        }

        private async void OnRateTrack(MusicTrack track)
        {
            if (track == null) return;
            track.Rating = (track.Rating + 1) % 6;
            await _trackRepository.UpdateMetadataAsync(track.Id, track.Rating);
        }

        private void OnAnalyzeTrack(MusicTrack track)
        {
            if (track == null) return;
            
            // 1. 导航到详情页
            var parameters = new NavigationParameters();
            parameters.Add("track", track);
            parameters.Add("autoPlay", false);
            _regionManager.RequestNavigate("LibraryContentRegion", "DetailView", parameters);

            // 2. 发送分析信号 (这一步可以通过 Event 延迟触发，或者在 LoopWorkspaceViewModel 里监听状态变化)
            // 鉴于目前的 UI 结构，直接跳转到详情页已经让 LoopWorkspace 加载了数据
            // 我们只需要通过事件告诉工作区“开始分析”即可
            _eventAggregator.GetEvent<seamless_loop_music.Events.TrackMetadataChangedEvent>().Publish(track);
        }

        private async void OnBatchAnalyze()
        {
            var tracksToAnalyze = SelectedTracks.Any() ? SelectedTracks.ToList() : Tracks.ToList();
            if (!tracksToAnalyze.Any()) return;

            IsAnalyzing = true;
            var loc = LocalizationService.Instance;
            StatusMessage = loc["StatusPreparing"];

            var progress = new Progress<(int current, int total, string fileName)>(p =>
            {
                var msg = string.Format(loc["StatusAnalyzing"], p.current, p.total, p.fileName);
                _eventAggregator.GetEvent<StatusMessageEvent>().Publish(msg);
            });

            try
            {
                await _playerService.AnalyzeTracksAsync(tracksToAnalyze, progress);
                var doneMsg = string.Format(loc["StatusAnalyzeDone"], tracksToAnalyze.Count);
                _eventAggregator.GetEvent<StatusMessageEvent>().Publish(doneMsg);
                
                // 延迟清除状态消息
                await Task.Delay(3000);
                _eventAggregator.GetEvent<StatusMessageEvent>().Publish(string.Empty);
            }
            catch (Exception ex)
            {
                var errMsg = string.Format(loc["StatusAnalyzeError"], ex.Message);
                _eventAggregator.GetEvent<StatusMessageEvent>().Publish(errMsg);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private void OnShowInExplorer(MusicTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
            catch { }
        }

        private void OnRenameAlias(MusicTrack track)
        {
            if (track == null) return;

            var loc = LocalizationService.Instance;
            var dialog = new InputDialog(loc["DialogRenameAlias"], loc["PromptNewName"], track.DisplayName ?? track.Title);
            if (dialog.ShowDialog() == true)
            {
                var newAlias = dialog.InputText;
                track.DisplayName = newAlias;
                
                Task.Run(async () => 
                {
                    await _trackRepository.UpdateMetadataAsync(track);
                    // 同步到播放引擎
                    if (_playbackService.CurrentTrack?.Id == track.Id)
                    {
                        _playbackService.CurrentTrack.DisplayName = newAlias;
                    }
                    // 发送全局通知
                    _eventAggregator.GetEvent<TrackMetadataChangedEvent>().Publish(track);
                });
            }
        }

        private void OnAddToPlaylist(MusicTrack track)
        {
            var tracksToAdd = SelectedTracks.Count > 1 ? SelectedTracks.ToList() : new List<MusicTrack> { track ?? SelectedTrack };
            if (!tracksToAdd.Any() || tracksToAdd.Any(t => t == null)) return;

            var dialog = new Views.AddToPlaylistDialog(_playlistManager, _eventAggregator, tracksToAdd);
            dialog.Owner = App.Current.MainWindow;
            dialog.ShowDialog();
        }

        private async void OnRemoveFromList(MusicTrack track)
        {
            var tracksToRemove = SelectedTracks.Count > 1 ? SelectedTracks.ToList() : new List<MusicTrack> { track ?? SelectedTrack };
            if (!tracksToRemove.Any() || tracksToRemove.Any(t => t == null)) return;

            if (_selectedCategoryItem != null && _selectedCategoryItem.Type == CategoryType.Playlist && _selectedCategoryItem.Id > 0)
            {
                var loc = LocalizationService.Instance;
                var msg = string.Format(loc["MsgConfirmRemove"], tracksToRemove.Count, _selectedCategoryItem.Name);
                var result = System.Windows.MessageBox.Show(msg, loc["SettingsTitle"], System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    foreach (var t in tracksToRemove)
                    {
                        await _playlistManager.RemoveTrackFromPlaylistAsync(_selectedCategoryItem.Id, t.Id);
                    }
                    await ReloadTracksAsync(); // 重新加载以更新视图
                }
            }
            else
            {
                System.Windows.MessageBox.Show(LocalizationService.Instance["TipCustomPlaylistOnly"], LocalizationService.Instance["SettingsTitle"], System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private async void OnDeleteFromDisk(MusicTrack track)
        {
            var tracksToDelete = SelectedTracks.Count > 1 ? SelectedTracks.ToList() : new List<MusicTrack> { track ?? SelectedTrack };
            if (!tracksToDelete.Any() || tracksToDelete.Any(t => t == null)) return;

            var loc = LocalizationService.Instance;
            var msg = string.Format(loc["MsgConfirmDelete"], tracksToDelete.Count);
            var result = System.Windows.MessageBox.Show(msg, loc["SettingsTitle"], System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                foreach (var t in tracksToDelete)
                {
                    try
                    {
                        if (System.IO.File.Exists(t.FilePath))
                        {
                            // 使用 Microsoft.VisualBasic 移动到回收站
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(t.FilePath, 
                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, 
                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        }
                        await _trackRepository.DeleteAsync(t.Id);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"删除文件 {t.FileName} 失败: {ex.Message}");
                    }
                }
                await ReloadTracksAsync();
            }
        }

        private void OnPlaySelected()
        {
            if (SelectedTracks.Any())
            {
                _playbackService.SetQueue(SelectedTracks.ToList(), SelectedTracks.First());
                _playbackService.LoadTrackAsync(SelectedTracks.First(), true);
            }
        }

        private void OnSelectAll()
        {
            SelectedTracks.Clear();
            foreach (var track in TracksView.Cast<MusicTrack>())
            {
                SelectedTracks.Add(track);
            }
        }

        private void OnTrackMetadataChanged(MusicTrack track)
        {
            var local = Tracks.FirstOrDefault(t => t.Id == track.Id);
            if (local != null && local != track)
            {
                local.Rating = track.Rating;
            }
        }

        public async void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 处理导航参数
            if (navigationContext.Parameters.ContainsKey("compact"))
            {
                IsCompact = (bool)navigationContext.Parameters["compact"];
            }

            // 优先恢复分类上下文
            if (navigationContext.Parameters.ContainsKey("category"))
            {
                var category = navigationContext.Parameters["category"] as CategoryItem;
                await ApplyCategoryFilterAsync(category);
            }

            // 只有在列表为空时才重新加载，防止返回时丢失位置
            if (Tracks.Count == 0)
            {
                await ReloadTracksAsync();
            }

            // 如果导航带了 track 参数，自动选中
            if (navigationContext.Parameters.ContainsKey("track"))
            {
                var targetTrack = navigationContext.Parameters["track"] as MusicTrack;
                if (targetTrack != null)
                {
                    SelectedTrack = Tracks.FirstOrDefault(t => t.Id == targetTrack.Id);
                    
                    // 如果是在侧边栏精简模式，且没有分类参数，尝试自动关联专辑
                    if (IsCompact && navigationContext.Parameters.ContainsKey("category") == false)
                    {
                        var category = new CategoryItem 
                        { 
                            Name = targetTrack.Album, 
                            Type = CategoryType.Album 
                        };
                        await ApplyCategoryFilterAsync(category);
                    }
                }
            }

            // 确保刷新视图
            App.Current.Dispatcher.Invoke(() => 
            {
                UpdateStats();
                TracksView.Refresh();

                // 进入页面时，通知视图滚动到正在播放的曲目（如果在这张列表里的话）
                if (_playbackService.CurrentTrack != null)
                {
                    var playingTrack = Tracks.FirstOrDefault(t => t.Id == _playbackService.CurrentTrack.Id);
                    if (playingTrack != null)
                    {
                        Task.Run(async () => 
                        {
                            await Task.Delay(100);
                            _eventAggregator.GetEvent<ScrollToTrackEvent>().Publish(playingTrack);
                        });
                    }
                }
            });
        }

        private async Task ReloadTracksAsync()
        {
            var oldSelectedId = SelectedTrack?.Id;
            var results = await _trackRepository.GetAllAsync();
            
            App.Current.Dispatcher.Invoke(() =>
            {
                Tracks.Clear();
                foreach (var t in results) Tracks.Add(t);
                
                if (oldSelectedId.HasValue)
                {
                    SelectedTrack = Tracks.FirstOrDefault(t => t.Id == oldSelectedId.Value);
                }

                UpdatePlayingStatus();
                UpdateStats();
                TracksView.Refresh();

                // 列表加载完成后，通知视图滚动到正在播放的曲目（如果在这张列表里的话）
                if (_playbackService.CurrentTrack != null)
                {
                    var playingTrack = Tracks.FirstOrDefault(t => t.Id == _playbackService.CurrentTrack.Id);
                    if (playingTrack != null)
                    {
                        // 稍微延迟确保容器已经开始生成
                        Task.Run(async () => 
                        {
                            await Task.Delay(100);
                            _eventAggregator.GetEvent<ScrollToTrackEvent>().Publish(playingTrack);
                        });
                    }
                }
            });
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
