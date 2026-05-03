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
using seamless_loop_music.UI.Views;

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
        private readonly IFoldersService _foldersService;
        
        private bool _isInternalSelectionChange = false;
        
        // --- 三栏式导航新增属性 ---
        private ObservableCollection<CategoryNavTarget> _navigationCategories;
        public ObservableCollection<CategoryNavTarget> NavigationCategories
        {
            get => _navigationCategories;
            set => SetProperty(ref _navigationCategories, value);
        }

        private CategoryNavTarget _selectedCategory;
        public CategoryNavTarget SelectedCategory
        {
            get => _selectedCategory;
            set 
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    _ = LoadCategoryItems();
                    RaisePropertyChanged(nameof(IsPlaylistCategorySelected));
                }
            }
        }

        public bool IsPlaylistCategorySelected => SelectedCategory?.Type == CategoryType.Playlist;

        private ObservableCollection<CategoryItem> _categoryItems = new ObservableCollection<CategoryItem>();
        public ObservableCollection<CategoryItem> CategoryItems
        {
            get => _categoryItems;
            set => SetProperty(ref _categoryItems, value);
        }

        private ICollectionView _categoryItemsView;
        public ICollectionView CategoryItemsView
        {
            get => _categoryItemsView;
            set => SetProperty(ref _categoryItemsView, value);
        }

        private CategoryItem _selectedCategoryItem;
        public CategoryItem SelectedCategoryItem
        {
            get => _selectedCategoryItem;
            set 
            {
                if (SetProperty(ref _selectedCategoryItem, value))
                {
                    if (!_isInternalSelectionChange && value != null)
                    {
                        // 发布事件通知子区域刷新
                        _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Publish(value);
                    }
                    
                    // 通知右键菜单状态更新
                    RenamePlaylistCommand?.RaiseCanExecuteChanged();
                    DeletePlaylistCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _middleFilterText;
        public string MiddleFilterText
        {
            get => _middleFilterText;
            set 
            {
                if (SetProperty(ref _middleFilterText, value))
                {
                    CategoryItemsView?.Refresh();
                }
            }
        }

        // --- 文件夹导航属性 ---
        private ObservableCollection<SubfolderItem> _rootFolders = new ObservableCollection<SubfolderItem>();
        public ObservableCollection<SubfolderItem> RootFolders => _rootFolders;

        private SubfolderItem _selectedRoot;
        public SubfolderItem SelectedRoot
        {
            get => _selectedRoot;
            set
            {
                if (SetProperty(ref _selectedRoot, value) && value != null)
                {
                    _ = LoadSubfoldersAsync(value.Path);
                }
            }
        }

        private ObservableCollection<SubfolderItem> _subfolders = new ObservableCollection<SubfolderItem>();
        public ObservableCollection<SubfolderItem> Subfolders => _subfolders;

        private ICollectionView _subfoldersView;
        public ICollectionView SubfoldersView
        {
            get => _subfoldersView;
            set => SetProperty(ref _subfoldersView, value);
        }

        private string _folderFilterText;
        public string FolderFilterText
        {
            get => _folderFilterText;
            set
            {
                if (SetProperty(ref _folderFilterText, value))
                {
                    SubfoldersView?.Refresh();
                }
            }
        }

        private ObservableCollection<SubfolderItem> _breadcrumbs = new ObservableCollection<SubfolderItem>();
        public ObservableCollection<SubfolderItem> Breadcrumbs => _breadcrumbs;

        public bool IsFolderCategorySelected => SelectedCategory?.Type == CategoryType.Folder;

        public DelegateCommand<string> NavigateToPathCommand { get; }
        public DelegateCommand NavigateUpCommand { get; }

        

        public LibraryViewModel(ITrackRepository trackRepository, IPlaybackService playbackService, IPlaylistManager playlistManager, IRegionManager regionManager, ISearchService searchService, IEventAggregator eventAggregator, IFoldersService foldersService)
        {
            _trackRepository = trackRepository;
            _playbackService = playbackService;
            _playlistManager = playlistManager;
            _regionManager = regionManager;
            _searchService = searchService;
            _eventAggregator = eventAggregator;
            _foldersService = foldersService;

            NavigateToPathCommand = new DelegateCommand<string>(path => _ = LoadSubfoldersAsync(path));
            NavigateUpCommand = new DelegateCommand(OnNavigateUp, () => Breadcrumbs.Count > 1);

            InitializeNavigationCategories();

            // Initialize Category View
            CategoryItemsView = CollectionViewSource.GetDefaultView(CategoryItems);
            CategoryItemsView.Filter = (obj) => 
            {
                if (string.IsNullOrWhiteSpace(MiddleFilterText)) return true;
                if (obj is CategoryItem item) return item.Name?.IndexOf(MiddleFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                return false;
            };

            // Initialize Subfolders View
            SubfoldersView = CollectionViewSource.GetDefaultView(Subfolders);
            SubfoldersView.Filter = (obj) => 
            {
                if (string.IsNullOrWhiteSpace(FolderFilterText)) return true;
                if (obj is SubfolderItem item) return item.Name?.IndexOf(FolderFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                return false;
            };

            // 初始化命令后设置默认选中（触发 LoadCategoryItems）
            PlayCategoryItemCommand = new DelegateCommand<CategoryItem>(OnPlayCategoryItem);
            RenamePlaylistCommand = new DelegateCommand(OnRenamePlaylist, () => SelectedCategoryItem != null && SelectedCategoryItem.Type == CategoryType.Playlist && SelectedCategoryItem.Id > 0);
            DeletePlaylistCommand = new DelegateCommand(OnDeletePlaylist, () => SelectedCategoryItem != null && SelectedCategoryItem.Type == CategoryType.Playlist && SelectedCategoryItem.Id > 0);
            CreatePlaylistCommand = new DelegateCommand(OnCreatePlaylist);
            
            // 监听语言切换事件
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(c => 
            {
                // 记住当前的选中状态
                var currentNavType = SelectedCategory?.Type;
                var currentItem = SelectedCategoryItem;

                InitializeNavigationCategories();
                
                if (currentNavType.HasValue)
                {
                    SelectedCategory = NavigationCategories.FirstOrDefault(n => n.Type == currentNavType.Value);
                    _ = LoadCategoryItems(currentItem);
                }
            });

            // 设置默认选中（这会触发初始加载）
            SelectedCategory = NavigationCategories.FirstOrDefault();

            // 初始导航到曲目列表
            _regionManager.RequestNavigate("LibraryContentRegion", "TrackListView");

            // 扫描完成后自动刷新分类列表
            _eventAggregator.GetEvent<LibraryRefreshedEvent>().Subscribe(() => { var _ = LoadCategoryItems(); });

            // 监听外部触发的分类选中（用于状态还原）
            _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Subscribe(async item => 
            {
                if (item == null || _isInternalSelectionChange) return;

                _isInternalSelectionChange = true;
                try
                {
                    // 如果当前选中的分类大类不对，则先切换大类
                    if (SelectedCategory == null || SelectedCategory.Type != item.Type)
                    {
                        var targetNav = NavigationCategories.FirstOrDefault(n => n.Type == item.Type);
                        if (targetNav != null)
                        {
                            _selectedCategory = targetNav; // 直接设字段，避免 setter 触发额外的 Load
                            RaisePropertyChanged(nameof(SelectedCategory));
                            RaisePropertyChanged(nameof(IsPlaylistCategorySelected));
                            await LoadCategoryItems(item);
                        }
                    }
                    else if (SelectedCategoryItem == null || 
                            (SelectedCategoryItem.Type == CategoryType.Playlist && SelectedCategoryItem.Id != item.Id) ||
                            (SelectedCategoryItem.Type != CategoryType.Playlist && SelectedCategoryItem.Name != item.Name))
                    {
                        await LoadCategoryItems(item);
                    }
                }
                finally
                {
                    _isInternalSelectionChange = false;
                }
            }, ThreadOption.UIThread);
        }

        public DelegateCommand<CategoryItem> PlayCategoryItemCommand { get; }
        public DelegateCommand RenamePlaylistCommand { get; }
        public DelegateCommand DeletePlaylistCommand { get; }
        public DelegateCommand CreatePlaylistCommand { get; }
        public DelegateCommand RefreshPlaylistCommand => new DelegateCommand(() => { var _ = LoadCategoryItems(); });

        private async void OnPlayCategoryItem(CategoryItem item)
        {
            if (item == null) return;

            switch (item.Type)
            {
                case CategoryType.Artist:
                    await _playbackService.EnqueueArtistAsync(item.Name);
                    break;
                case CategoryType.Album:
                    await _playbackService.EnqueueAlbumAsync(item.Name, item.Description);
                    break;
                case CategoryType.Playlist:
                    await _playbackService.EnqueuePlaylistAsync(item);
                    break;
            }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 如果指定了目标子视图，则跳转到该视图
            if (navigationContext.Parameters.ContainsKey("target"))
            {
                var target = navigationContext.Parameters["target"] as string;
                _regionManager.RequestNavigate("LibraryContentRegion", target, navigationContext.Parameters);
            }
            else
            {
                // 默认在右侧区域打开歌曲列表
                _regionManager.RequestNavigate("LibraryContentRegion", "TrackListView");
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private void InitializeNavigationCategories()
        {
            var loc = LocalizationService.Instance;
            NavigationCategories = new ObservableCollection<CategoryNavTarget>
            {
                new CategoryNavTarget { Name = loc["NavAlbum"], Icon = "💿", Type = CategoryType.Album },
                new CategoryNavTarget { Name = loc["NavArtist"], Icon = "👤", Type = CategoryType.Artist },
                new CategoryNavTarget { Name = loc["NavPlaylist"], Icon = "📂", Type = CategoryType.Playlist },
                new CategoryNavTarget { Name = loc["NavFolder"], Icon = "📁", Type = CategoryType.Folder }
            };
        }

        private async Task LoadCategoryItems(CategoryItem targetToSelect = null)
        {
            CategoryItems.Clear();
            var allTracks = await _trackRepository.GetAllAsync();
            
            IEnumerable<CategoryItem> items = null;
            var loc = LocalizationService.Instance;

            switch (SelectedCategory.Type)
            {
                case CategoryType.Album:
                    items = allTracks
                        .Where(t => !string.IsNullOrEmpty(t.Album))
                        .GroupBy(t => new { t.Album, t.Artist })
                        .Select(g => 
                        {
                            // 优先使用 Albums 表记录的封面，若无则从该专辑下找第一个有封面的曲目作为兜底
                            var officialCover = g.FirstOrDefault()?.AlbumCoverPath;
                            var fallbackCover = string.IsNullOrEmpty(officialCover) 
                                ? g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverPath))?.CoverPath 
                                : officialCover;

                            return new CategoryItem 
                            { 
                                Name = g.Key.Album, 
                                Description = g.Key.Artist,
                                ImagePath = fallbackCover,
                                Type = CategoryType.Album
                            };
                        });
                    break;
                case CategoryType.Artist:
                    items = allTracks
                        .Where(t => !string.IsNullOrEmpty(t.Artist))
                        .GroupBy(t => t.Artist)
                        .Select(g => 
                        {
                            // 艺术家封面优先级：艺术家表记录的封面 -> 该艺术家下第一个有封面的曲目
                            var officialCover = g.FirstOrDefault()?.ArtistCoverPath;
                            var fallbackCover = string.IsNullOrEmpty(officialCover) 
                                ? g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverPath))?.CoverPath 
                                : officialCover;

                            return new CategoryItem 
                            { 
                                Name = g.Key, 
                                Type = CategoryType.Artist,
                                Icon = "👤",
                                ImagePath = fallbackCover 
                            };
                        });
                    break;
                case CategoryType.Playlist:
                    var playlists = await _playlistManager.GetAllPlaylistsAsync();
                    var list = new List<CategoryItem>
                    {
                        new CategoryItem { Id = -1, Name = loc["PlaylistAll"], Icon = "🎶", Type = CategoryType.Playlist },
                        new CategoryItem { Id = -2, Name = loc["PlaylistFavorites"], Icon = "⭐", Type = CategoryType.Playlist }
                    };
                    list.AddRange(playlists.Select(p => new CategoryItem 
                    { 
                        Id = p.Id,
                        Name = p.Name, 
                        Icon = "📂",
                        Type = CategoryType.Playlist,
                        ImagePath = null 
                    }));
                    items = list;
                    break;
                case CategoryType.Folder:
                    await LoadRootFoldersAsync();
                    break;
            }

            if (items != null)
            {
                foreach (var item in items) CategoryItems.Add(item);
            }

            CategoryItemsView.Refresh();
            RaisePropertyChanged(nameof(IsFolderCategorySelected));

            // 如果有指定要选中的项，则选中它
            if (targetToSelect != null)
            {
                var found = CategoryItems.FirstOrDefault(i => 
                    (i.Type == CategoryType.Playlist && i.Id == targetToSelect.Id) || 
                    (i.Type != CategoryType.Playlist && i.Name == targetToSelect.Name));
                
                if (found != null)
                {
                    SelectedCategoryItem = found;
                    return;
                }
            }

            // 否则默认选中第一项
            SelectedCategoryItem = CategoryItems.FirstOrDefault();
        }

        private async Task LoadRootFoldersAsync()
        {
            var roots = await _foldersService.GetRootFoldersAsync();
            App.Current.Dispatcher.Invoke(() => 
            {
                RootFolders.Clear();
                foreach (var r in roots) RootFolders.Add(r);
                SelectedRoot = RootFolders.FirstOrDefault();
            });
        }

        private async Task LoadSubfoldersAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var sub = await _foldersService.GetSubfoldersAsync(path);
            var crumbs = _foldersService.GetBreadcrumbs(path).ToList();

            App.Current.Dispatcher.Invoke(() => 
            {
                Subfolders.Clear();
                foreach (var s in sub) Subfolders.Add(s);

                Breadcrumbs.Clear();
                foreach (var c in crumbs) Breadcrumbs.Add(c);
                NavigateUpCommand.RaiseCanExecuteChanged();
            });

            // 通知右侧列表更新路径
            var parameters = new NavigationParameters();
            parameters.Add("folderPath", path);
            _regionManager.RequestNavigate("LibraryContentRegion", "TrackListView", parameters);
        }

        private void OnNavigateUp()
        {
            if (Breadcrumbs.Count <= 1) return;
            var parent = Breadcrumbs[Breadcrumbs.Count - 2];
            _ = LoadSubfoldersAsync(parent.Path);
        }

        private void OnRenamePlaylist()
        {
            if (SelectedCategoryItem == null || SelectedCategoryItem.Type != CategoryType.Playlist || SelectedCategoryItem.Id <= 0) return;

            var loc = LocalizationService.Instance;
            var dialog = new InputDialog(loc["DialogRenamePlaylist"], loc["PromptNewName"], SelectedCategoryItem.Name);
            if (dialog.ShowDialog() == true)
            {
                var newName = dialog.InputText;
                if (string.IsNullOrWhiteSpace(newName)) return;

                var id = SelectedCategoryItem.Id;
                Task.Run(async () => {
                    // 全局查重（直接查数据库）
                    var allPlaylists = await _playlistManager.GetAllPlaylistsAsync();
                    if (allPlaylists.Any(p => p.Id != id && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        App.Current.Dispatcher.Invoke(() => 
                            System.Windows.MessageBox.Show(string.Format(loc["MsgPlaylistExists"], newName), 
                            loc["SettingsTitle"], System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning));
                        return;
                    }

                    await _playlistManager.RenamePlaylistAsync(id, newName);
                    // 刷新列表
                    App.Current.Dispatcher.Invoke(() => { _ = LoadCategoryItems(); });
                });
            }
        }

        private void OnDeletePlaylist()
        {
            if (SelectedCategoryItem == null || SelectedCategoryItem.Type != CategoryType.Playlist || SelectedCategoryItem.Id <= 0) return;

            var loc = LocalizationService.Instance;
            var result = System.Windows.MessageBox.Show(string.Format(loc["MsgConfirmDeletePlaylist"], SelectedCategoryItem.Name), 
                loc["SettingsTitle"], System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                var id = SelectedCategoryItem.Id;
                Task.Run(async () => {
                    await _playlistManager.DeletePlaylistAsync(id);
                    // 刷新列表
                    App.Current.Dispatcher.Invoke(() => { _ = LoadCategoryItems(); });
                });
            }
        }

        private void OnCreatePlaylist()
        {
            var loc = LocalizationService.Instance;
            var dialog = new InputDialog(loc["DialogNewPlaylist"], loc["PromptPlaylistName"], loc["DefaultPlaylistName"]);
            if (dialog.ShowDialog() == true)
            {
                var name = dialog.InputText;
                if (string.IsNullOrWhiteSpace(name)) return;

                Task.Run(async () => {
                    // 全局查重（直接查数据库）
                    var allPlaylists = await _playlistManager.GetAllPlaylistsAsync();
                    if (allPlaylists.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        App.Current.Dispatcher.Invoke(() => 
                            System.Windows.MessageBox.Show(string.Format(loc["MsgPlaylistExists"], name), 
                            loc["SettingsTitle"], System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning));
                        return;
                    }

                    await _playlistManager.CreatePlaylistAsync(name);
                    // 刷新列表
                    App.Current.Dispatcher.Invoke(() => { _ = LoadCategoryItems(); });
                });
            }
        }

        
    }
}
