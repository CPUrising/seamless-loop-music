using System.Data;
using Dapper;

namespace seamless_loop_music.Data
{
    public static class PlaybackStatisticsSyncSchema
    {
        public static void EnsureSchema(IDbConnection db)
        {
            db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackSyncDevices (
                DeviceId TEXT PRIMARY KEY NOT NULL,
                CurrentGeneration INTEGER NOT NULL CHECK(CurrentGeneration >= 0),
                DisplayName TEXT NULL,
                DisplayNameUpdatedAtUtcMs INTEGER NOT NULL CHECK(DisplayNameUpdatedAtUtcMs >= 0),
                Platform TEXT NOT NULL CHECK(Platform IN ('android', 'windows')),
                FirstSeenAtUtcMs INTEGER NOT NULL CHECK(FirstSeenAtUtcMs >= 0),
                LastSeenAtUtcMs INTEGER NOT NULL CHECK(LastSeenAtUtcMs >= FirstSeenAtUtcMs)
            );");
            db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackSyncSongs (
                SongId INTEGER PRIMARY KEY AUTOINCREMENT,
                NormalizedFileName TEXT NOT NULL,
                FileName TEXT NOT NULL,
                DurationMs INTEGER NOT NULL CHECK(DurationMs >= 0),
                TotalSamples INTEGER NULL CHECK(TotalSamples IS NULL OR TotalSamples >= 0),
                ContentHash TEXT NULL,
                LocalTrackId INTEGER NULL,
                UNIQUE(NormalizedFileName, DurationMs),
                FOREIGN KEY(LocalTrackId) REFERENCES Tracks(Id) ON DELETE SET NULL
            );");
            db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackSyncContributions (
                SongId INTEGER NOT NULL,
                DeviceId TEXT NOT NULL,
                Generation INTEGER NOT NULL CHECK(Generation >= 0),
                UndatedListenMs INTEGER NOT NULL DEFAULT 0 CHECK(UndatedListenMs >= 0),
                FirstPlayedAtUtcMs INTEGER NULL CHECK(FirstPlayedAtUtcMs IS NULL OR FirstPlayedAtUtcMs >= 0),
                LastPlayedAtUtcMs INTEGER NULL CHECK(LastPlayedAtUtcMs IS NULL OR LastPlayedAtUtcMs >= 0),
                UpdatedAtUtcMs INTEGER NOT NULL CHECK(UpdatedAtUtcMs >= 0),
                PRIMARY KEY(SongId, DeviceId, Generation),
                CHECK(FirstPlayedAtUtcMs IS NULL OR LastPlayedAtUtcMs IS NULL OR FirstPlayedAtUtcMs <= LastPlayedAtUtcMs),
                FOREIGN KEY(SongId) REFERENCES PlaybackSyncSongs(SongId) ON DELETE CASCADE,
                FOREIGN KEY(DeviceId) REFERENCES PlaybackSyncDevices(DeviceId) ON DELETE NO ACTION
            );");
            db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackSyncDailyBuckets (
                SongId INTEGER NOT NULL,
                DeviceId TEXT NOT NULL,
                Generation INTEGER NOT NULL CHECK(Generation >= 0),
                LocalDate TEXT NOT NULL CHECK(length(LocalDate) = 10 AND LocalDate GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND strftime('%Y-%m-%d', LocalDate) = LocalDate),
                ListenMs INTEGER NOT NULL CHECK(ListenMs >= 0),
                PRIMARY KEY(SongId, DeviceId, Generation, LocalDate),
                FOREIGN KEY(SongId, DeviceId, Generation) REFERENCES PlaybackSyncContributions(SongId, DeviceId, Generation) ON DELETE CASCADE
            );");
            db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackSyncTombstones (
                DeviceId TEXT NOT NULL,
                Generation INTEGER NOT NULL CHECK(Generation >= 0),
                Scope TEXT NOT NULL CHECK(Scope = 'deviceGeneration'),
                TombstonedAtUtcMs INTEGER NOT NULL CHECK(TombstonedAtUtcMs >= 0),
                TombstonedByDeviceId TEXT NOT NULL,
                Reason TEXT NOT NULL,
                PRIMARY KEY(DeviceId, Generation, Scope),
                FOREIGN KEY(DeviceId) REFERENCES PlaybackSyncDevices(DeviceId) ON DELETE NO ACTION,
                FOREIGN KEY(TombstonedByDeviceId) REFERENCES PlaybackSyncDevices(DeviceId) ON DELETE NO ACTION
            );");
            db.Execute(@"CREATE TABLE IF NOT EXISTS PlaybackStatisticsSettlements (
                SettlementEventId TEXT PRIMARY KEY NOT NULL,
                SongId INTEGER NOT NULL,
                DeviceId TEXT NOT NULL,
                Generation INTEGER NOT NULL CHECK(Generation >= 0),
                AppliedAtUtcMs INTEGER NOT NULL CHECK(AppliedAtUtcMs >= 0),
                SourceKind TEXT NOT NULL,
                Diagnostics TEXT NULL,
                FOREIGN KEY(SongId) REFERENCES PlaybackSyncSongs(SongId) ON DELETE NO ACTION,
                FOREIGN KEY(DeviceId) REFERENCES PlaybackSyncDevices(DeviceId) ON DELETE NO ACTION
            );");
            // Older developer databases may still have the one-song-per-track index.
            db.Execute("DROP INDEX IF EXISTS idx_playbacksyncsongs_localtrackid_unique;");
            db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksyncsongs_localtrackid ON PlaybackSyncSongs(LocalTrackId);");
            db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksynccontributions_device_generation ON PlaybackSyncContributions(DeviceId, Generation);");
            db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksyncbuckets_localdate_songid ON PlaybackSyncDailyBuckets(LocalDate, SongId);");
            db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksyncbuckets_songid_localdate ON PlaybackSyncDailyBuckets(SongId, LocalDate);");
            db.Execute("CREATE INDEX IF NOT EXISTS idx_playbacksynctombstones_device_generation ON PlaybackSyncTombstones(DeviceId, Generation);");
        }
    }
}
