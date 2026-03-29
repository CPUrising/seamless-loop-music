using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Events;
using seamless_loop_music.UI.Views;
using System.Windows;

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaylistSidebarViewModel : BindableBase
    {
        private readonly IPlaylistManager _playlistManager;
        private readonly IPlaybackService _playbackService;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        private ObservableCollection<Playlist> _playlists;
        public ObservableCollection<Playlist> Playlists
        {
            get => _playlists;
            set => SetProperty(ref _playlists, value);
        }

        private Playlist _selectedPlaylist;
        public Playlist SelectedPlaylist
        {
            get => _selectedPlaylist;
            set
            {
                if (SetProperty(ref _selectedPlaylist, value))
                {
                    OnPlaylistSelected(value);
                }
            }
        }

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand AddPlaylistCommand { get; }
        public DelegateCommand RenamePlaylistCommand { get; }
        public DelegateCommand DeletePlaylistCommand { get; }
        public DelegateCommand RefreshPlaylistCommand => RefreshCommand;

        public PlaylistSidebarViewModel(IPlaylistManager playlistManager, IPlaybackService playbackService, IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _playlistManager = playlistManager;
            _playbackService = playbackService;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;

            Playlists = new ObservableCollection<Playlist>();
            
            RefreshCommand = new DelegateCommand(async () => await LoadPlaylistsAsync());
            AddPlaylistCommand = new DelegateCommand(OnAddPlaylist);
            RenamePlaylistCommand = new DelegateCommand(OnRenamePlaylist, () => SelectedPlaylist != null && SelectedPlaylist.Id > 0);
            DeletePlaylistCommand = new DelegateCommand(OnDeletePlaylist, () => SelectedPlaylist != null && SelectedPlaylist.Id > 0);

            // 订阅数据变动事件
            _eventAggregator.GetEvent<PlaylistChangedEvent>().Subscribe(async () => await LoadPlaylistsAsync());

            // 初始加载
            Task.Run(async () => await LoadPlaylistsAsync());
        }

        private async Task LoadPlaylistsAsync()
        {
            var allPlaylists = await _playlistManager.GetAllPlaylistsAsync();
            App.Current.Dispatcher.Invoke(() =>
            {
                Playlists.Clear();
                
                // 添加“我的最爱”虚拟歌单
                Playlists.Add(new Playlist { Id = -1, Name = "我的最爱 ❤️" });

                foreach (var p in allPlaylists)
                {
                    Playlists.Add(p);
                }
            });
        }

        private void OnPlaylistSelected(Playlist playlist)
        {
            if (playlist == null) return;

            var parameters = new NavigationParameters();
            parameters.Add("PlaylistId", playlist.Id);
            parameters.Add("PlaylistName", playlist.Name);
            
            _regionManager.RequestNavigate("MainContentRegion", "LibraryView", parameters);
            
            // 通知命令状态更新
            RenamePlaylistCommand.RaiseCanExecuteChanged();
            DeletePlaylistCommand.RaiseCanExecuteChanged();
        }

        private void OnAddPlaylist()
        {
            var dialog = new InputDialog("新建歌单", "请输入歌单名称：");
            if (dialog.ShowDialog() == true)
            {
                var name = dialog.InputText;
                Task.Run(async () => await _playlistManager.CreatePlaylistAsync(name));
            }
        }

        private void OnRenamePlaylist()
        {
            if (SelectedPlaylist == null || SelectedPlaylist.Id <= 0) return;

            var dialog = new InputDialog("重命名歌单", "请输入新的名称：", SelectedPlaylist.Name);
            if (dialog.ShowDialog() == true)
            {
                var newName = dialog.InputText;
                var id = SelectedPlaylist.Id;
                Task.Run(async () => await _playlistManager.RenamePlaylistAsync(id, newName));
            }
        }

        private void OnDeletePlaylist()
        {
            if (SelectedPlaylist == null || SelectedPlaylist.Id <= 0) return;

            var result = MessageBox.Show($"确定要删除歌单「{SelectedPlaylist.Name}」吗喵？(>﹏<)", 
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var id = SelectedPlaylist.Id;
                Task.Run(async () => await _playlistManager.DeletePlaylistAsync(id));
            }
        }
    }
}
