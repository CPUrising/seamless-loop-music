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
                    LoadCategoryItems();
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
                    // 发布事件通知子区域刷新
                    _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Publish(value);
                    
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

        

        public LibraryViewModel(ITrackRepository trackRepository, IPlaybackService playbackService, IPlaylistManager playlistManager, IRegionManager regionManager, ISearchService searchService, IEventAggregator eventAggregator)
        {
            _trackRepository = trackRepository;
            _playbackService = playbackService;
            _playlistManager = playlistManager;
            _regionManager = regionManager;
            _searchService = searchService;
            _eventAggregator = eventAggregator;

            // 初始化导航分类
            NavigationCategories = new ObservableCollection<CategoryNavTarget>
            {
                new CategoryNavTarget { Name = "专辑", Icon = "💿", Type = CategoryType.Album },
                new CategoryNavTarget { Name = "艺术家", Icon = "👤", Type = CategoryType.Artist },
                new CategoryNavTarget { Name = "歌单", Icon = "📂", Type = CategoryType.Playlist }
            };

            // Initialize Category View
            CategoryItemsView = CollectionViewSource.GetDefaultView(CategoryItems);
            CategoryItemsView.Filter = (obj) => 
            {
                if (string.IsNullOrWhiteSpace(MiddleFilterText)) return true;
                if (obj is CategoryItem item) return item.Name?.IndexOf(MiddleFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                return false;
            };

            // 必须在设置 SelectedCategory 之前初始化命令，因为后者会触发 LoadCategoryItems 并最终调用命令的 RaiseCanExecuteChanged
            PlayCategoryItemCommand = new DelegateCommand<CategoryItem>(OnPlayCategoryItem);
            RenamePlaylistCommand = new DelegateCommand(OnRenamePlaylist, () => SelectedCategoryItem != null && SelectedCategoryItem.Type == CategoryType.Playlist && SelectedCategoryItem.Id > 0);
            DeletePlaylistCommand = new DelegateCommand(OnDeletePlaylist, () => SelectedCategoryItem != null && SelectedCategoryItem.Type == CategoryType.Playlist && SelectedCategoryItem.Id > 0);
            CreatePlaylistCommand = new DelegateCommand(OnCreatePlaylist);

            // 设置默认选中（这会触发 LoadCategoryItems）
            SelectedCategory = NavigationCategories.FirstOrDefault();

            // 扫描完成后自动刷新分类列表
            _eventAggregator.GetEvent<LibraryRefreshedEvent>().Subscribe(() => LoadCategoryItems());
        }

        public DelegateCommand<CategoryItem> PlayCategoryItemCommand { get; }
        public DelegateCommand RenamePlaylistCommand { get; }
        public DelegateCommand DeletePlaylistCommand { get; }
        public DelegateCommand CreatePlaylistCommand { get; }
        public DelegateCommand RefreshPlaylistCommand => new DelegateCommand(() => LoadCategoryItems());

        private async void OnPlayCategoryItem(CategoryItem item)
        {
            if (item == null) return;

            switch (item.Type)
            {
                case CategoryType.Artist:
                    await _playbackService.EnqueueArtistAsync(item.Name);
                    break;
                case CategoryType.Album:
                    await _playbackService.EnqueueAlbumAsync(item.Name);
                    break;
                case CategoryType.Playlist:
                    await _playbackService.EnqueuePlaylistAsync(item);
                    break;
            }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 默认在右侧区域打开歌曲列表
            _regionManager.RequestNavigate("LibraryContentRegion", "TrackListView");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private async void LoadCategoryItems()
        {
            CategoryItems.Clear();
            var allTracks = await _trackRepository.GetAllAsync();
            
            IEnumerable<CategoryItem> items = null;

            switch (SelectedCategory.Type)
            {
                case CategoryType.Album:
                    items = allTracks
                        .Where(t => !string.IsNullOrEmpty(t.Album))
                        .GroupBy(t => t.Album)
                        .Select(g => new CategoryItem 
                        { 
                            Name = g.Key, 
                            Type = CategoryType.Album,
                            Icon = "💿",
                            ImagePath = g.FirstOrDefault()?.FilePath 
                        });
                    break;
                case CategoryType.Artist:
                    items = allTracks
                        .Where(t => !string.IsNullOrEmpty(t.Artist))
                        .GroupBy(t => t.Artist)
                        .Select(g => new CategoryItem 
                        { 
                            Name = g.Key, 
                            Type = CategoryType.Artist,
                            Icon = "👤",
                            ImagePath = g.FirstOrDefault()?.FilePath 
                        });
                    break;
                case CategoryType.Playlist:
                    var playlists = await _playlistManager.GetAllPlaylistsAsync();
                    var list = new List<CategoryItem>
                    {
                        new CategoryItem { Id = -1, Name = "全部歌曲", Icon = "🎶", Type = CategoryType.Playlist },
                        new CategoryItem { Id = -2, Name = "我的收藏", Icon = "❤️", Type = CategoryType.Playlist }
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
            }

            if (items != null)
            {
                foreach (var item in items) CategoryItems.Add(item);
            }

            CategoryItemsView.Refresh();
            SelectedCategoryItem = CategoryItems.FirstOrDefault();
        }

        private void OnRenamePlaylist()
        {
            if (SelectedCategoryItem == null || SelectedCategoryItem.Type != CategoryType.Playlist || SelectedCategoryItem.Id <= 0) return;

            var dialog = new InputDialog("重命名歌单", "请输入新的名称：", SelectedCategoryItem.Name);
            if (dialog.ShowDialog() == true)
            {
                var newName = dialog.InputText;
                var id = SelectedCategoryItem.Id;
                Task.Run(async () => {
                    await _playlistManager.RenamePlaylistAsync(id, newName);
                    // 刷新列表
                    App.Current.Dispatcher.Invoke(() => LoadCategoryItems());
                });
            }
        }

        private void OnDeletePlaylist()
        {
            if (SelectedCategoryItem == null || SelectedCategoryItem.Type != CategoryType.Playlist || SelectedCategoryItem.Id <= 0) return;

            var result = System.Windows.MessageBox.Show($"确定要删除歌单「{SelectedCategoryItem.Name}」吗喵？(>﹏<)", 
                "确认删除", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                var id = SelectedCategoryItem.Id;
                Task.Run(async () => {
                    await _playlistManager.DeletePlaylistAsync(id);
                    // 刷新列表
                    App.Current.Dispatcher.Invoke(() => LoadCategoryItems());
                });
            }
        }

        private void OnCreatePlaylist()
        {
            var dialog = new InputDialog("新建歌单", "请输入歌单名称：", "新歌单");
            if (dialog.ShowDialog() == true)
            {
                var name = dialog.InputText;
                if (string.IsNullOrWhiteSpace(name)) return;

                Task.Run(async () => {
                    await _playlistManager.CreatePlaylistAsync(name);
                    // 刷新列表
                    App.Current.Dispatcher.Invoke(() => LoadCategoryItems());
                });
            }
        }

        
    }
}
