using System;
using System.Collections.Generic;
using System.Linq;
using MaterialDesignThemes.Wpf;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.ViewModels.Settings
{
    public enum PlaybackStatisticsSourceDeviceRowKind
    {
        Device,
        DeletedDataSummary
    }

    public sealed class PlaybackStatisticsSourceDeviceRow : BindableBase
    {
        private readonly PlaybackStatisticsSourceDevice _source;
        private bool _isSelected;
        private string _displayName;
        private string _platformText;
        private string _localBadgeText;
        private string _effectiveListeningDurationText;
        private string _secondaryText;

        public PlaybackStatisticsSourceDeviceRow(PlaybackStatisticsSourceDevice source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            RowKind = PlaybackStatisticsSourceDeviceRowKind.Device;
            DeviceId = source.DeviceId ?? string.Empty;
            IsLocalDevice = source.IsLocalDevice;
            CanSelect = !IsLocalDevice;
            CanRename = true;
            PlatformKey = NormalizePlatform(source.Platform);
            PlatformIconKind = ResolvePlatformIconKind(PlatformKey);
            RefreshLocalizedText();
        }

        private PlaybackStatisticsSourceDeviceRow(int deletedSourceCount)
        {
            RowKind = PlaybackStatisticsSourceDeviceRowKind.DeletedDataSummary;
            DeletedSourceCount = deletedSourceCount;
            DeviceId = string.Empty;
            PlatformKey = string.Empty;
            PlatformIconKind = PackIconKind.DeleteOutline;
            RefreshLocalizedText();
        }

        public static PlaybackStatisticsSourceDeviceRow CreateDeletedDataSummary(int deletedSourceCount)
        {
            if (deletedSourceCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(deletedSourceCount));

            return new PlaybackStatisticsSourceDeviceRow(deletedSourceCount);
        }

        public static List<PlaybackStatisticsSourceDeviceRow> CreateRows(IEnumerable<PlaybackStatisticsSourceDevice> sources)
        {
            var activeRows = sources
                .Where(x => x.IsLocalDevice || x.KnownActiveGenerationCount > 0)
                .OrderByDescending(x => x.IsLocalDevice)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.DeviceId : x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.DeviceId, StringComparer.Ordinal)
                .Select(x => new PlaybackStatisticsSourceDeviceRow(x))
                .ToList();
            var deletedSourceCount = sources.Count(x => !x.IsLocalDevice && x.KnownActiveGenerationCount == 0);
            if (deletedSourceCount > 0)
                activeRows.Add(CreateDeletedDataSummary(deletedSourceCount));

            return activeRows;
        }

        public PlaybackStatisticsSourceDeviceRowKind RowKind { get; }
        public bool IsDeletedDataSummary => RowKind == PlaybackStatisticsSourceDeviceRowKind.DeletedDataSummary;
        public string DeviceId { get; }
        public bool IsLocalDevice { get; }
        public bool CanSelect { get; }
        public bool CanRename { get; }
        public string PlatformKey { get; }
        public PackIconKind PlatformIconKind { get; }
        public int DeletedSourceCount { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                var next = CanSelect && value;
                SetProperty(ref _isSelected, next);
            }
        }

        public string DisplayName
        {
            get => _displayName;
            private set => SetProperty(ref _displayName, value);
        }

        public string PlatformText
        {
            get => _platformText;
            private set => SetProperty(ref _platformText, value);
        }

        public string LocalBadgeText
        {
            get => _localBadgeText;
            private set => SetProperty(ref _localBadgeText, value);
        }

        public string EffectiveListeningDurationText
        {
            get => _effectiveListeningDurationText;
            private set => SetProperty(ref _effectiveListeningDurationText, value);
        }

        public string SecondaryText
        {
            get => _secondaryText;
            private set => SetProperty(ref _secondaryText, value);
        }

        public string RenameSeedText => _source == null || string.IsNullOrWhiteSpace(_source.DisplayName) ? DisplayName : _source.DisplayName;

        public void RefreshLocalizedText()
        {
            var loc = LocalizationService.Instance;
            if (IsDeletedDataSummary)
            {
                DisplayName = loc["PlaybackStatisticsSourceDeviceDeletedDataSummaryTitle"];
                SecondaryText = string.Format(loc["PlaybackStatisticsSourceDeviceDeletedDataSummaryCount"], DeletedSourceCount);
                PlatformText = string.Empty;
                LocalBadgeText = string.Empty;
                EffectiveListeningDurationText = string.Empty;
                return;
            }

            var suffix = DeviceSuffix(DeviceId);
            PlatformText = LocalizedPlatformName(loc, PlatformKey);
            DisplayName = string.IsNullOrWhiteSpace(_source.DisplayName)
                ? BuildFallbackName(loc, PlatformKey, suffix)
                : _source.DisplayName.Trim();
            LocalBadgeText = IsLocalDevice ? loc["PlaybackStatisticsSourceDeviceLocalBadge"] : string.Empty;
            EffectiveListeningDurationText = PlaybackStatisticsTrack.FormatDuration(_source.EffectiveTotalListenMs);
            SecondaryText = string.Format(loc["PlaybackStatisticsSourceDeviceSecondaryFormat"], PlatformText, suffix);
        }

        private static string BuildFallbackName(LocalizationService loc, string platformKey, string suffix)
        {
            switch (platformKey)
            {
                case "windows":
                    return string.Format(loc["PlaybackStatisticsSourceDeviceFallbackWindows"], suffix);
                case "android":
                    return string.Format(loc["PlaybackStatisticsSourceDeviceFallbackAndroid"], suffix);
                default:
                    return string.Format(loc["PlaybackStatisticsSourceDeviceFallbackGeneric"], suffix);
            }
        }

        private static string LocalizedPlatformName(LocalizationService loc, string platformKey)
        {
            switch (platformKey)
            {
                case "windows":
                    return loc["PlaybackStatisticsSourceDevicePlatformWindows"];
                case "android":
                    return loc["PlaybackStatisticsSourceDevicePlatformAndroid"];
                default:
                    return loc["PlaybackStatisticsSourceDevicePlatformUnknown"];
            }
        }

        private static PackIconKind ResolvePlatformIconKind(string platformKey)
        {
            switch (platformKey)
            {
                case "windows":
                    return PackIconKind.MicrosoftWindows;
                case "android":
                    return PackIconKind.Android;
                default:
                    return PackIconKind.Devices;
            }
        }

        private static string NormalizePlatform(string platform)
        {
            if (string.IsNullOrWhiteSpace(platform))
                return string.Empty;

            var normalized = platform.Trim().ToLowerInvariant();
            if (normalized.Contains("windows"))
                return "windows";
            if (normalized.Contains("android"))
                return "android";
            return normalized;
        }

        private static string DeviceSuffix(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return "----";

            var value = deviceId.Trim();
            return value.Length <= 4 ? value : value.Substring(value.Length - 4);
        }
    }
}
