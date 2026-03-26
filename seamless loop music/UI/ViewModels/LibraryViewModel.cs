using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Data.Repositories;

namespace seamless_loop_music.UI.ViewModels
{
    public class LibraryViewModel : BindableBase
    {
        private readonly ITrackRepository _trackRepository;
        private readonly IPlaybackService _playbackService;
        private readonly IPlaylistManager _playlistManager;
        private readonly IRegionManager _regionManager;
        
        private ObservableCollection<MusicTrack> _tracks = new ObservableCollection<MusicTrack>();
        public ObservableCollection<MusicTrack> Tracks
        {
            get => _tracks;
            set => SetProperty(ref _tracks, value);
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set {
                if (SetProperty(ref _searchText, value))
                {
                    FilterTracks();
                }
            }
        }

        public DelegateCommand<MusicTrack> PlayCommand { get; }
        public DelegateCommand<MusicTrack> OpenDetailCommand { get; }
        public DelegateCommand RefreshCommand { get; }

        public LibraryViewModel(ITrackRepository trackRepository, IPlaybackService playbackService, IPlaylistManager playlistManager, IRegionManager regionManager)
        {
            _trackRepository = trackRepository;
            _playbackService = playbackService;
            _playlistManager = playlistManager;
            _regionManager = regionManager;

            PlayCommand = new DelegateCommand<MusicTrack>(OnPlayTrack);
            OpenDetailCommand = new DelegateCommand<MusicTrack>(OnOpenDetail);
            RefreshCommand = new DelegateCommand(async () => await LoadTracksAsync());

            // Initial load
            Task.Run(async () => await LoadTracksAsync());
        }

        private async Task LoadTracksAsync()
        {
            var allTracks = await _trackRepository.GetAllAsync();
            App.Current.Dispatcher.Invoke(() =>
            {
                Tracks.Clear();
                foreach (var track in allTracks)
                {
                    Tracks.Add(track);
                }
            });
        }

        private void FilterTracks()
        {
            // TODO
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
            
            var parameters = new NavigationParameters();
            parameters.Add("track", track);
            _regionManager.RequestNavigate("MainContentRegion", "DetailView", parameters);
        }
    }
}
