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
