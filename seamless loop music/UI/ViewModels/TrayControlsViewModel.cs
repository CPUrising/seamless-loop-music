using System;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels
{
    public class TrayControlsViewModel : BindableBase
    {
        private readonly IPlaybackService _playbackService;
        private MusicTrack _currentTrack;
        private bool _isPlaying;

        public MusicTrack CurrentTrack
        {
            get => _currentTrack;
            set => SetProperty(ref _currentTrack, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }

        public TrayControlsViewModel(IPlaybackService playbackService)
        {
            _playbackService = playbackService;

            CurrentTrack = _playbackService.CurrentTrack;
            IsPlaying = _playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing;

            _playbackService.TrackChanged += track => CurrentTrack = track;
            _playbackService.StateChanged += state => IsPlaying = state == NAudio.Wave.PlaybackState.Playing;

            PlayPauseCommand = new DelegateCommand(() => {
                if (IsPlaying) _playbackService.Pause();
                else _playbackService.Play();
            });

            NextCommand = new DelegateCommand(() => _playbackService.Next());
            PreviousCommand = new DelegateCommand(() => _playbackService.Previous());
        }
    }
}
