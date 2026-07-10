using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaybackStatisticsViewModel : BindableBase, INavigationAware
    {
        private readonly IPlaybackStatisticsRepository _statisticsRepository;
        private int _loadVersion;
        private PlaybackStatisticsPeriod _selectedPeriod = PlaybackStatisticsPeriod.Day;
        private bool _isLoading;

        public ObservableCollection<PlaybackStatisticsTrack> TopTracks { get; } = new ObservableCollection<PlaybackStatisticsTrack>();
        public ObservableCollection<PlaybackStatisticsTrack> MostListenedTracks { get; } = new ObservableCollection<PlaybackStatisticsTrack>();
        public DelegateCommand<PlaybackStatisticsPeriod?> SelectPeriodCommand { get; }

        public PlaybackStatisticsPeriod SelectedPeriod
        {
            get => _selectedPeriod;
            private set
            {
                if (SetProperty(ref _selectedPeriod, value))
                {
                    RaisePeriodStateProperties();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool HasStatistics => MostListenedTracks.Count > 0;
        public long TotalListeningDurationMs { get; private set; }
        public int TrackedTrackCount { get; private set; }

        private string _totalListeningDurationText;
        public string TotalListeningDurationText
        {
            get => _totalListeningDurationText;
            private set => SetProperty(ref _totalListeningDurationText, value);
        }

        private string _trackedTrackCountText;
        public string TrackedTrackCountText
        {
            get => _trackedTrackCountText;
            private set => SetProperty(ref _trackedTrackCountText, value);
        }
        public bool IsDaySelected => SelectedPeriod == PlaybackStatisticsPeriod.Day;
        public bool IsWeekSelected => SelectedPeriod == PlaybackStatisticsPeriod.Week;
        public bool IsMonthSelected => SelectedPeriod == PlaybackStatisticsPeriod.Month;
        public bool IsYearSelected => SelectedPeriod == PlaybackStatisticsPeriod.Year;
        public bool IsAllSelected => SelectedPeriod == PlaybackStatisticsPeriod.All;

        public PlaybackStatisticsViewModel(IPlaybackStatisticsRepository statisticsRepository, IEventAggregator eventAggregator)
        {
            _statisticsRepository = statisticsRepository;
            SelectPeriodCommand = new DelegateCommand<PlaybackStatisticsPeriod?>(SelectPeriod);
            eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(_ =>
            {
                foreach (var track in MostListenedTracks)
                {
                    track.RefreshLocalizedText();
                }
                RefreshSummaryText();
            }, ThreadOption.UIThread);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _ = LoadAsync();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private void SelectPeriod(PlaybackStatisticsPeriod? period)
        {
            if (!period.HasValue || (SelectedPeriod == period.Value && HasStatistics)) return;

            SelectedPeriod = period.Value;
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            var requestVersion = ++_loadVersion;
            IsLoading = true;

            try
            {
                var results = await _statisticsRepository.GetTopTracksAsync(SelectedPeriod, int.MaxValue);
                if (requestVersion != _loadVersion) return;

                PopulateTracks(CreateRankedTracks(results));
            }
            catch
            {
                if (requestVersion != _loadVersion) return;

                PopulateTracks(CreateRankedTracks(Enumerable.Empty<PlaybackStatisticItem>()));
            }
            finally
            {
                if (requestVersion == _loadVersion)
                {
                    IsLoading = false;
                }
            }
        }

        public static PlaybackStatisticsTrackCollections CreateRankedTracks(IEnumerable<PlaybackStatisticItem> items)
        {
            var rankedItems = (items ?? Enumerable.Empty<PlaybackStatisticItem>()).ToList();
            var maxDurationMs = rankedItems.Count == 0 ? 0 : rankedItems[0].TotalDurationMs;
            var tracks = rankedItems
                .Select((item, index) => new PlaybackStatisticsTrack(item, index + 1, maxDurationMs))
                .ToList();

            return new PlaybackStatisticsTrackCollections(tracks.Take(5).ToList(), tracks, rankedItems.Sum(item => item.TotalDurationMs), tracks.Count);
        }

        private void PopulateTracks(PlaybackStatisticsTrackCollections tracks)
        {
            TopTracks.Clear();
            MostListenedTracks.Clear();

            foreach (var track in tracks.TopTracks)
            {
                TopTracks.Add(track);
            }

            foreach (var track in tracks.MostListenedTracks)
            {
                MostListenedTracks.Add(track);
            }

            TotalListeningDurationMs = tracks.TotalListeningDurationMs;
            TrackedTrackCount = tracks.MostListenedTracks.Count;
            RaisePropertyChanged(nameof(HasStatistics));
            RaisePropertyChanged(nameof(TotalListeningDurationMs));
            RaisePropertyChanged(nameof(TrackedTrackCount));
            RefreshSummaryText();
        }

        private void RefreshSummaryText()
        {
            TotalListeningDurationText = PlaybackStatisticsTrack.FormatDuration(TotalListeningDurationMs);
            TrackedTrackCountText = string.Format(LocalizationService.Instance["StatisticsTrackedTracksFormat"], TrackedTrackCount);
        }

        private void RaisePeriodStateProperties()
        {
            RaisePropertyChanged(nameof(IsDaySelected));
            RaisePropertyChanged(nameof(IsWeekSelected));
            RaisePropertyChanged(nameof(IsMonthSelected));
            RaisePropertyChanged(nameof(IsYearSelected));
            RaisePropertyChanged(nameof(IsAllSelected));
        }
    }

    public class PlaybackStatisticsTrackCollections
    {
        public PlaybackStatisticsTrackCollections(IReadOnlyList<PlaybackStatisticsTrack> topTracks, IReadOnlyList<PlaybackStatisticsTrack> mostListenedTracks, long totalListeningDurationMs, int trackedTrackCount)
        {
            TopTracks = topTracks;
            MostListenedTracks = mostListenedTracks;
            TotalListeningDurationMs = totalListeningDurationMs;
            TrackedTrackCount = trackedTrackCount;
        }

        public IReadOnlyList<PlaybackStatisticsTrack> TopTracks { get; }
        public IReadOnlyList<PlaybackStatisticsTrack> MostListenedTracks { get; }
        public long TotalListeningDurationMs { get; }
        public int TrackedTrackCount { get; }
    }

    public class PlaybackStatisticsTrack : BindableBase
    {
        public PlaybackStatisticsTrack(PlaybackStatisticItem item, int rank, long maxDurationMs)
        {
            Rank = rank;
            TrackId = item.TrackId;
            Title = item.Title;
            Artist = item.Artist;
            Album = item.Album;
            CoverPath = TrackCoverResolver.Resolve(item.TrackCoverPath, item.AlbumCoverPath, item.ArtistCoverPath);
            TotalDurationMs = item.TotalDurationMs;
            BarPercent = CalculateBarPercent(TotalDurationMs, maxDurationMs);
            BarOpacity = rank == 1 ? 1.0 : 0.68;
            RefreshLocalizedText();
        }

        public int Rank { get; }
        public int TrackId { get; }
        public string Title { get; }
        public string Artist { get; }
        public string Album { get; }
        public string CoverPath { get; }
        public long TotalDurationMs { get; }
        public double BarPercent { get; }
        public double BarOpacity { get; }

        private string _durationText;
        public string DurationText
        {
            get => _durationText;
            private set => SetProperty(ref _durationText, value);
        }

        public void RefreshLocalizedText()
        {
            DurationText = FormatDuration(TotalDurationMs);
        }

        public static double CalculateBarPercent(long durationMs, long maxDurationMs)
        {
            const double minimumVisiblePercent = 10;

            if (durationMs <= 0 || maxDurationMs <= 0)
            {
                return 0;
            }

            var percent = durationMs / (double)maxDurationMs * 100;
            return Math.Min(100, Math.Max(minimumVisiblePercent, percent));
        }

        public static string FormatDuration(long totalDurationMs)
        {
            var totalSeconds = totalDurationMs > 0
                ? Math.Max(1, totalDurationMs / 1000)
                : 0;
            var loc = LocalizationService.Instance;

            if (totalSeconds < 60)
            {
                return string.Format(loc["StatisticsDurationSeconds"], totalSeconds);
            }

            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            if (minutes < 60)
            {
                return seconds == 0
                    ? string.Format(loc["StatisticsDurationMinutes"], minutes)
                    : string.Format(loc["StatisticsDurationMinutesSeconds"], minutes, seconds);
            }

            var hours = minutes / 60;
            minutes %= 60;
            return minutes == 0
                ? string.Format(loc["StatisticsDurationHours"], hours)
                : string.Format(loc["StatisticsDurationHoursMinutes"], hours, minutes);
        }
    }
}
