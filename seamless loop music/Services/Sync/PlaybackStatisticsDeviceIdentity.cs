using System;

namespace seamless_loop_music.Services.Sync
{
    internal static class PlaybackStatisticsDeviceIdentity
    {
        public const string DeviceKey = "Sync.DeviceId";

        public static string CurrentWindowsDisplayName()
        {
            var machineName = Environment.MachineName;
            return string.IsNullOrWhiteSpace(machineName) ? "Windows device" : machineName.Trim();
        }
    }
}
