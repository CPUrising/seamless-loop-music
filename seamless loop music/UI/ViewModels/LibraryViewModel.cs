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
                }
            }
        }

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

            // 设置默认选中
            SelectedCategory = NavigationCategories.FirstOrDefault();

            
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
                            ImagePath = g.FirstOrDefault()?.FilePath // 暂时用第一个曲目
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
                            ImagePath = g.FirstOrDefault()?.FilePath 
                        });
                    break;
                case CategoryType.Playlist:
                    var playlists = await _playlistManager.GetAllPlaylistsAsync();
                    items = playlists.Select(p => new CategoryItem 
                    { 
                        Id = p.Id,
                        Name = p.Name, 
                        Type = CategoryType.Playlist,
                        ImagePath = null 
                    });
                    break;
            }

            if (items != null)
            {
                foreach (var item in items) CategoryItems.Add(item);
            }

            CategoryItemsView.Refresh();
            SelectedCategoryItem = CategoryItems.FirstOrDefault();
        }

        
    }
}
