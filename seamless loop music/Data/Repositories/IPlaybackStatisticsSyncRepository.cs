using seamless_loop_music.Models;
using System.Collections.Generic;

namespace seamless_loop_music.Data.Repositories
{
    public interface IPlaybackStatisticsSyncRepository
    {
        PlaybackSyncDevice EnsureDevice(PlaybackSyncDevice device);
        PlaybackSyncDevice EnsureLocalDevice(string deviceId, string displayName, long seenAtUtcMs);
        PlaybackSyncDevice MergeOwnerAuthoredDeviceRegistration(PlaybackSyncDevice device);
        PlaybackSyncDevice AdvanceLocalDeviceGeneration(string deviceId, long generation, long seenAtUtcMs);
        PlaybackSyncSong EnsureSong(PlaybackSyncSong song);
        PlaybackSyncSong EnsureSongBoundToTrack(PlaybackSyncSong song, int localTrackId);
        void MergeContribution(PlaybackSyncContribution contribution);
        void InsertTombstone(PlaybackSyncTombstone tombstone);
        bool RecordSettlement(PlaybackSyncSettlement settlement, long listenMs, string localDate, long? firstPlayedAtUtcMs, long? lastPlayedAtUtcMs);
        PlaybackStatisticsGenerationClearResult TombstoneAndRotateLocalGeneration(string deviceId, long tombstonedAtUtcMs);
        PlaybackStatisticsTombstoneObservationResult ObserveCurrentGenerationTombstone(string deviceId, long observedGeneration, long observedAtUtcMs);
        int RelinkSongs();
        IReadOnlyList<PlaybackStatisticsSourceDevice> GetSourceDevices(string localDeviceId);
        PlaybackSyncDevice RenameDevice(string deviceId, string displayName, long updatedAtUtcMs);
        int TombstoneKnownActiveGenerations(IEnumerable<string> deviceIds, long tombstonedAtUtcMs, string actorDeviceId, string reason, string localDeviceId);
        int TombstoneAllKnownNonLocalGenerations(long tombstonedAtUtcMs, string actorDeviceId, string reason, string localDeviceId);
        PlaybackSyncPersistedState LoadState();
    }
}
