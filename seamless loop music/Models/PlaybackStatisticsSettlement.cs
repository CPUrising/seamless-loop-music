using System;

namespace seamless_loop_music.Models
{
    // Local persistence contract. This deliberately is not a GitHub wire DTO.
    public sealed class PlaybackStatisticsSettlement
    {
        public string SettlementEventId { get; set; }
        public string FileName { get; set; }
        public string NormalizedFileName { get; set; }
        public long TrackDurationMs { get; set; }
        public long? TotalSamples { get; set; }
        public string ContentHash { get; set; }
        public int? LocalTrackId { get; set; }
        public string DeviceId { get; set; }
        public long Generation { get; set; }
        public string SourceLocalDate { get; set; }
        public long StartedAtUtcMs { get; set; }
        public long DurationMs { get; set; }
        public long AppliedAtUtcMs { get; set; }
        public string SourceKind { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SettlementEventId) || string.IsNullOrWhiteSpace(FileName) ||
                string.IsNullOrWhiteSpace(NormalizedFileName) || string.IsNullOrWhiteSpace(DeviceId) ||
                string.IsNullOrWhiteSpace(SourceKind) || TrackDurationMs < 0 || Generation < 0 ||
                StartedAtUtcMs < 0 || DurationMs <= 0 || AppliedAtUtcMs < 0 ||
                (TotalSamples.HasValue && TotalSamples.Value < 0) || (LocalTrackId.HasValue && LocalTrackId.Value <= 0) ||
                DurationMs > long.MaxValue - StartedAtUtcMs)
                throw new ArgumentException("Invalid playback statistics settlement.");
            if (SourceLocalDate != null && !DateTime.TryParseExact(SourceLocalDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
                throw new ArgumentException("SourceLocalDate must be yyyy-MM-dd.");
        }
    }

    public sealed class PlaybackStatisticsOutboxState
    {
        public int Version { get; set; } = 2;
        public System.Collections.Generic.List<PlaybackStatisticsSettlement> SettlementEvents { get; set; } = new System.Collections.Generic.List<PlaybackStatisticsSettlement>();
    }

    public sealed class PlaybackStatisticsGenerationClearResult
    {
        public long OldGeneration { get; set; }
        public long NewGeneration { get; set; }
        public int AffectedContributionCount { get; set; }
        public int AffectedCount { get => AffectedContributionCount; set => AffectedContributionCount = value; }
    }

    public sealed class PlaybackStatisticsTombstoneObservationResult
    {
        public bool Rotated { get; set; }
        public string DeviceId { get; set; }
        public long OldGeneration { get; set; }
        public long NewGeneration { get; set; }
        public int AffectedContributionCount { get; set; }
    }

    public sealed class PlaybackStatisticsSourceDevice
    {
        public string DeviceId { get; set; }
        public string DisplayName { get; set; }
        public string Platform { get; set; }
        public bool IsLocalDevice { get; set; }
        public long CurrentGeneration { get; set; }
        public long EffectiveTotalListenMs { get; set; }
        public int KnownActiveGenerationCount { get; set; }
    }
}
