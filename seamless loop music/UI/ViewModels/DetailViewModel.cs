using System;
using System.IO;
using System.Windows.Media.Imaging;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Commands;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using TagLib;

namespace seamless_loop_music.UI.ViewModels
{
    public class DetailViewModel : BindableBase, INavigationAware
    {
        private readonly IPlaybackService _playbackService;
        private readonly IRegionManager _regionManager;
        
        private MusicTrack _currentTrack;
        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        private BitmapImage _albumCoverImage;
        public BitmapImage AlbumCoverImage
        {
            get => _albumCoverImage;
            set => SetProperty(ref _albumCoverImage, value);
        }

        private string _albumInfo;
        public string AlbumInfo
        {
            get => _albumInfo;
            set => SetProperty(ref _albumInfo, value);
        }

        public DelegateCommand GoBackCommand { get; }

        public DetailViewModel(IPlaybackService playbackService, IRegionManager regionManager)
        {
            _playbackService = playbackService;
            _regionManager = regionManager;
            GoBackCommand = new DelegateCommand(OnGoBack);
            _playbackService.TrackChanged += OnTrackChanged;
        }

        private void OnTrackChanged(MusicTrack track)
        {
            if (track != null)
            {
                CurrentTrack = track;
                LoadAlbumCover(track);
                UpdateAlbumInfo(track);
            }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.ContainsKey("track"))
            {
                var track = navigationContext.Parameters["track"] as MusicTrack;
                if (track != null)
                {
                    CurrentTrack = track;
                    LoadAlbumCover(track);
                    UpdateAlbumInfo(track);
                    _playbackService.LoadTrackAsync(track, true).ConfigureAwait(false);
                }
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _playbackService.TrackChanged -= OnTrackChanged;
        }

        private void LoadAlbumCover(MusicTrack track)
        {
            try
            {
                if (!System.IO.File.Exists(track.FilePath)) return;

                using (var file = TagLib.File.Create(track.FilePath))
                {
                    if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                    {
                        var picture = file.Tag.Pictures[0];
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = new MemoryStream(picture.Data.Data);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 400;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        AlbumCoverImage = bitmap;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[专辑封面加载失败] {ex.Message}");
            }
            AlbumCoverImage = null;
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
            _regionManager.RequestNavigate("MainContentRegion", "LibraryView");
        }
    }
}

