using System;
using Prism.Mvvm;
using Prism.Regions;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels
{
    public class DetailViewModel : BindableBase, INavigationAware
    {
        private readonly IPlaybackService _playbackService;
        
        private MusicTrack _currentTrack;
        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        public DetailViewModel(IPlaybackService playbackService)
        {
            _playbackService = playbackService;
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.ContainsKey("track"))
            {
                var track = navigationContext.Parameters["track"] as MusicTrack;
                if (track != null)
                {
                    CurrentTrack = track;
                    // 使用新的播放服务加载
                    _playbackService.LoadTrackAsync(track, true).ConfigureAwait(false);
                }
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}

