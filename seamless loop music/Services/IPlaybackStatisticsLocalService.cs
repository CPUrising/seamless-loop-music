using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IPlaybackStatisticsLocalService
    {
        PlaybackStatisticsRecordingContext GetRecordingContext();
        IReadOnlyList<PlaybackStatisticsSettlement> Split(DateTimeOffset sourceLocalStart, long startedAtUtcMs, long durationMs, PlaybackStatisticsSettlement template);
        Task<bool> ApplyAsync(PlaybackStatisticsSettlement settlement);
        Task<PlaybackStatisticsGenerationClearResult> ClearCurrentGenerationAsync();
        Task<PlaybackStatisticsTombstoneObservationResult> ObserveCurrentGenerationTombstoneAsync();
        Task<int> RelinkSongsAsync();
        Task<IReadOnlyList<PlaybackStatisticsSourceDevice>> GetSourceDevicesAsync();
        Task RenameDeviceAsync(string deviceId, string displayName, long updatedAtUtcMs);
        Task<int> TombstoneKnownActiveGenerationsAsync(IEnumerable<string> deviceIds, long tombstonedAtUtcMs, string actorDeviceId, string reason);
        Task<int> TombstoneAllKnownNonLocalGenerationsAsync(long tombstonedAtUtcMs, string actorDeviceId, string reason);
        Task<List<PlaybackStatisticItem>> GetTopTracksAsync(PlaybackStatisticsPeriod period, int limit = 5, DateTimeOffset? viewerLocalNow = null);
    }

    public sealed class PlaybackStatisticsRecordingContext { public string DeviceId { get; set; } public long CurrentGeneration { get; set; } }
}
