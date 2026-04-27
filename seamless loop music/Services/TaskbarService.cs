using System;
using System.Windows.Shell;
using System.Windows.Threading;
using NAudio.Wave;
using Prism.Events;
using seamless_loop_music.Events;

namespace seamless_loop_music.Services
{
    public class TaskbarService : ITaskbarService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaybackService _playbackService;
        private TaskbarItemInfo _taskbarItemInfo;
        private readonly DispatcherTimer _progressTimer;

        public TaskbarService(IEventAggregator eventAggregator, IPlaybackService playbackService)
        {
            _eventAggregator = eventAggregator;
            _playbackService = playbackService;

            _progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _progressTimer.Tick += ProgressTimer_Tick;

            _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Subscribe(OnPlaybackStateChanged);
            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(OnTrackLoaded);
        }

        public void Initialize(TaskbarItemInfo taskbarItemInfo)
        {
            _taskbarItemInfo = taskbarItemInfo;
            
            if (_taskbarItemInfo.ThumbButtonInfos.Count >= 3)
            {
                _taskbarItemInfo.ThumbButtonInfos[0].Click += (s, e) => _playbackService.Previous();
                _taskbarItemInfo.ThumbButtonInfos[1].Click += (s, e) => {
                    if (_playbackService.PlaybackState == PlaybackState.Playing)
                        _playbackService.Pause();
                    else
                        _playbackService.Play();
                };
                _taskbarItemInfo.ThumbButtonInfos[2].Click += (s, e) => _playbackService.Next();
            }

            UpdateTaskbarState(_playbackService.PlaybackState);
        }

        private void OnTrackLoaded(Models.MusicTrack track)
        {
            UpdateTaskbarState(_playbackService.PlaybackState);
        }

        private void OnPlaybackStateChanged(PlaybackState state)
        {
            UpdateTaskbarState(state);
        }

        private void UpdateTaskbarState(PlaybackState state)
        {
            if (_taskbarItemInfo == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Update Play/Pause icon
                if (_taskbarItemInfo.ThumbButtonInfos.Count >= 2)
                {
                    var playPauseBtn = _taskbarItemInfo.ThumbButtonInfos[1];
                    if (state == PlaybackState.Playing)
                    {
                        playPauseBtn.ImageSource = (System.Windows.Media.ImageSource)System.Windows.Application.Current.FindResource("DrawingIconPause");
                        playPauseBtn.Description = "Pause";
                    }
                    else
                    {
                        playPauseBtn.ImageSource = (System.Windows.Media.ImageSource)System.Windows.Application.Current.FindResource("DrawingIconPlay");
                        playPauseBtn.Description = "Play";
                    }
                }

                switch (state)
                {
                    case PlaybackState.Playing:
                        _taskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                        if (!_progressTimer.IsEnabled) _progressTimer.Start();
                        break;
                    case PlaybackState.Paused:
                        _taskbarItemInfo.ProgressState = TaskbarItemProgressState.Paused;
                        if (_progressTimer.IsEnabled) _progressTimer.Stop();
                        UpdateProgress(); // Final update for pause
                        break;
                    case PlaybackState.Stopped:
                    default:
                        _taskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                        if (_progressTimer.IsEnabled) _progressTimer.Stop();
                        _taskbarItemInfo.ProgressValue = 0;
                        break;
                }
            });
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            if (_taskbarItemInfo == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var total = _playbackService.TotalTime;
                if (total.TotalSeconds > 0)
                {
                    var progress = _playbackService.CurrentTime.TotalSeconds / total.TotalSeconds;
                    _taskbarItemInfo.ProgressValue = Math.Max(0, Math.Min(1, progress));
                }
                else
                {
                    _taskbarItemInfo.ProgressValue = 0;
                }
            });
        }
    }
}
