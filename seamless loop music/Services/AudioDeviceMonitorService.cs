using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace seamless_loop_music.Services
{
    public class AudioDeviceMonitorService : IDisposable, IMMNotificationClient
    {
        private readonly IPlaybackService _playbackService;
        private MMDeviceEnumerator _enumerator;

        public AudioDeviceMonitorService(IPlaybackService playbackService)
        {
            _playbackService = playbackService;

            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render) return;
            PauseIfPlaying();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Unplugged ||
                newState == DeviceState.NotPresent ||
                newState == DeviceState.Disabled)
            {
                PauseIfPlaying();
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { PauseIfPlaying(); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void PauseIfPlaying()
        {
            try
            {
                if (_playbackService.PlaybackState != PlaybackState.Playing) return;

                var app = System.Windows.Application.Current;
                if (app == null) return;

                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_playbackService.PlaybackState == PlaybackState.Playing)
                    {
                        _playbackService.Pause();
                    }
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioDeviceMonitor] Error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_enumerator != null)
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
                _enumerator.Dispose();
                _enumerator = null;
            }
        }
    }
}
