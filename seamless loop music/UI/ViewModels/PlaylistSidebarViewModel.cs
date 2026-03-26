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

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaylistSidebarViewModel : BindableBase
    {
        private readonly IPlaylistManager _playlistManager;
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

        private ObservableCollection<MusicTrack> _currentTracks;
        public ObservableCollection<MusicTrack> CurrentTracks
        {
            get => _currentTracks;
            set => SetProperty(ref _currentTracks, value);
        }

        private MusicTrack _selectedTrack;
        public MusicTrack SelectedTrack
        {
            get => _selectedTrack;
            set => SetProperty(ref _selectedTrack, value);
        }

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand AddPlaylistCommand { get; }
        public DelegateCommand<MusicTrack> PlayTrackCommand { get; }

        private readonly IPlaybackService _playbackService;

        public PlaylistSidebarViewModel(IPlaylistManager playlistManager, IPlaybackService playbackService, IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _playlistManager = playlistManager;
            _playbackService = playbackService;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;

            Playlists = new ObservableCollection<Playlist>();
            CurrentTracks = new ObservableCollection<MusicTrack>();
            
            RefreshCommand = new DelegateCommand(async () => await LoadPlaylistsAsync());
            AddPlaylistCommand = new DelegateCommand(OnAddPlaylist);
            PlayTrackCommand = new DelegateCommand<MusicTrack>(OnPlayTrack);

            // 初始加载
            Task.Run(async () => await LoadPlaylistsAsync());
        }

        private void OnPlayTrack(MusicTrack track)
        {
            if (track == null) return;
            _playbackService.LoadTrackAsync(track, true);
        }

        private async Task LoadPlaylistsAsync()
        {
            var allPlaylists = await _playlistManager.GetAllPlaylistsAsync();
            App.Current.Dispatcher.Invoke(() =>
            {
                Playlists.Clear();
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
            _regionManager.RequestNavigate("MainContentRegion", "LibraryView", parameters);
        }

        private void OnAddPlaylist()
        {
            // TODO: 弹出对话框创建一个新播放列表
        }
    }
}
