using System.Collections.Generic;

namespace seamless_loop_music.Models
{
    public class PlaybackSyncDevice { public string DeviceId { get; set; } public long CurrentGeneration { get; set; } public string DisplayName { get; set; } public long DisplayNameUpdatedAtUtcMs { get; set; } public string Platform { get; set; } public long FirstSeenAtUtcMs { get; set; } public long LastSeenAtUtcMs { get; set; } }
    public class PlaybackSyncSong { public long SongId { get; set; } public string NormalizedFileName { get; set; } public string FileName { get; set; } public long DurationMs { get; set; } public long? TotalSamples { get; set; } public string ContentHash { get; set; } public int? LocalTrackId { get; set; } }
    public class PlaybackSyncContribution { public long SongId { get; set; } public string DeviceId { get; set; } public long Generation { get; set; } public long UndatedListenMs { get; set; } public long? FirstPlayedAtUtcMs { get; set; } public long? LastPlayedAtUtcMs { get; set; } public long UpdatedAtUtcMs { get; set; } public List<PlaybackSyncDailyBucket> DailyBuckets { get; set; } = new List<PlaybackSyncDailyBucket>(); }
    public class PlaybackSyncDailyBucket { public long SongId { get; set; } public string DeviceId { get; set; } public long Generation { get; set; } public string LocalDate { get; set; } public long ListenMs { get; set; } }
    public class PlaybackSyncTombstone { public string DeviceId { get; set; } public long Generation { get; set; } public string Scope { get; set; } public long TombstonedAtUtcMs { get; set; } public string TombstonedByDeviceId { get; set; } public string Reason { get; set; } }
    public class PlaybackSyncSettlement { public string SettlementEventId { get; set; } public long SongId { get; set; } public string DeviceId { get; set; } public long Generation { get; set; } public long AppliedAtUtcMs { get; set; } public string SourceKind { get; set; } public string Diagnostics { get; set; } }
    public class PlaybackSyncPersistedState { public List<PlaybackSyncDevice> Devices { get; set; } = new List<PlaybackSyncDevice>(); public List<PlaybackSyncSong> Songs { get; set; } = new List<PlaybackSyncSong>(); public List<PlaybackSyncContribution> Contributions { get; set; } = new List<PlaybackSyncContribution>(); public List<PlaybackSyncTombstone> Tombstones { get; set; } = new List<PlaybackSyncTombstone>(); }
}
