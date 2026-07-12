using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using NAudio.Wave;
using NUnit.Framework;
using Prism.Events;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackServiceCheckpointTests
    {
        private string _dbPath;
        private PlaybackService _playback;
        private FailingPlaybackStatisticsLocalService _statistics;
        private PlaybackState _statisticsPlaybackState;
        private MusicTrack _statisticsCurrentTrack = new MusicTrack();

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"PlaybackCheckpoint_{Guid.NewGuid()}.db");
            var db = new DatabaseHelper(_dbPath);
            db.InitializeDatabase();
            var events = new EventAggregator();
            var trackRepository = new TrackRepository(_dbPath);
            var playlistRepository = new PlaylistRepository(_dbPath);
            var playlistManager = new PlaylistManager(playlistRepository, trackRepository, events);
            _statistics = new FailingPlaybackStatisticsLocalService();
            _statisticsPlaybackState = PlaybackState.Stopped;
            _playback = new PlaybackService(
                trackRepository,
                playlistManager,
                events,
                new QueueManager(),
                new TrackMetadataService(db, events),
                _statistics,
                () => _statisticsPlaybackState,
                () => _statisticsCurrentTrack);
        }

        [TearDown]
        public void TearDown()
        {
            _statistics.FailApply = false;
            _statistics.BlockApply = false;
            _statistics.AllowApply.TrySetResult(true);
            _playback?.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        [Test]
        public async Task CaptureCheckpoint_FailureDoesNotStopAudioRuntimeAndCanRetry()
        {
            var captureRan = false;
            _statistics.FailApply = true;
            AddPendingSettlement();

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
                {
                    captureRan = true;
                    return 1;
                }));
            Assert.That(captureRan, Is.False);

            _statistics.FailApply = false;
            var result = await _playback.CapturePlaybackStatisticsCheckpointAsync(() => Task.FromResult(2));

            Assert.That(result, Is.EqualTo(2));
        }

        [Test]
        public async Task CaptureCheckpoint_DrainsSettlementsBeforeInvokingCapture()
        {
            AddPendingSettlement();
            var appliedWhenCaptured = -1;

            var result = await _playback.CapturePlaybackStatisticsCheckpointAsync(() =>
            {
                appliedWhenCaptured = _statistics.ApplyCount;
                return Task.FromResult(7);
            });

            Assert.That(result, Is.EqualTo(7));
            Assert.That(appliedWhenCaptured, Is.EqualTo(1));
            Assert.That(PendingSettlements(), Is.Empty);
        }

        [Test]
        public async Task CaptureCheckpoint_CaptureExceptionRestoresLifecycleForLaterSettlement()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _playback.CapturePlaybackStatisticsCheckpointAsync<int>(() =>
                    throw new InvalidOperationException("capture failed")));

            Assert.That(GetPrivateField<bool>("_statisticsFlushing"), Is.False);
            Assert.That(GetPrivateField<bool>("_statisticsClearing"), Is.False);

            AddPendingSettlement("after-capture-failure");
            var result = await _playback.CapturePlaybackStatisticsCheckpointAsync(() => Task.FromResult(8));

            Assert.That(result, Is.EqualTo(8));
            Assert.That(PendingSettlements(), Is.Empty);
        }

        [Test]
        public async Task CaptureCheckpoint_SerializesWithClear()
        {
            _statistics.BlockClear = true;
            var clearTask = _playback.ClearPlaybackStatisticsAsync();
            await _statistics.ClearStarted.Task;

            var captureCalled = false;
            var captureTask = _playback.CapturePlaybackStatisticsCheckpointAsync(() =>
            {
                captureCalled = true;
                return Task.FromResult(1);
            });

            await Task.Delay(50);
            Assert.That(captureCalled, Is.False);

            _statistics.AllowClear.SetResult(true);
            await clearTask;
            await captureTask;
            Assert.That(captureCalled, Is.True);
        }

        [Test]
        public async Task CaptureCheckpoint_SerializesWithRotation()
        {
            _statistics.BlockObserve = true;
            var rotateTask = _playback.RotateIfCurrentGenerationTombstonedAsync();
            await _statistics.ObserveStarted.Task;

            var captureCalled = false;
            var captureTask = _playback.CapturePlaybackStatisticsCheckpointAsync(() =>
            {
                captureCalled = true;
                return Task.FromResult(1);
            });

            await Task.Delay(50);
            Assert.That(captureCalled, Is.False);

            _statistics.AllowObserve.SetResult(true);
            await rotateTask;
            await captureTask;
            Assert.That(captureCalled, Is.True);
        }

        [Test]
        public async Task CaptureCheckpoint_RestartsPlayingSegmentAndFencesClosedSettlementUntilExit()
        {
            _statisticsPlaybackState = PlaybackState.Playing;
            _statisticsCurrentTrack = new MusicTrack { Id = 42, FileName = "playing.mp3", DurationMs = 1000 };

            var appliedDuringCapture = -1;
            await _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
            {
                Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Not.Null);
                await Task.Delay(75);

                _statisticsPlaybackState = PlaybackState.Paused;
                InvokePrivate("HandlePlaybackStatisticsStateChanged", PlaybackState.Paused);
                appliedDuringCapture = _statistics.ApplyCount;
                Assert.That(_statistics.SplitDurations.Count, Is.EqualTo(1));
                Assert.That(_statistics.SplitDurations[0], Is.GreaterThan(0));
                return 9;
            });

            Assert.That(appliedDuringCapture, Is.EqualTo(0));
            await _playback.FlushPlaybackStatisticsAsync();
            Assert.That(_statistics.ApplyCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CaptureCheckpoint_StoppedWithLoadedTrackDoesNotStartSegment()
        {
            _statisticsPlaybackState = PlaybackState.Stopped;
            _statisticsCurrentTrack = new MusicTrack { Id = 42, FileName = "stopped.mp3", DurationMs = 1000 };

            await _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
            {
                Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Null);
                await Task.Delay(75);
                Assert.That(_statistics.SplitDurations, Is.Empty);
                return 1;
            });

            Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Null);
            Assert.That(_statistics.SplitDurations, Is.Empty);
        }

        [Test]
        public async Task CaptureCheckpoint_StoppedToPlayingTracksOnlyTimeAfterTransition()
        {
            _statisticsPlaybackState = PlaybackState.Stopped;
            _statisticsCurrentTrack = new MusicTrack { Id = 42, FileName = "resumed.mp3", DurationMs = 1000 };

            await _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
            {
                Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Null);

                await Task.Delay(40);
                _statisticsPlaybackState = PlaybackState.Playing;
                InvokePrivate("HandlePlaybackStatisticsStateChanged", PlaybackState.Playing);
                Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Not.Null);

                await Task.Delay(75);
                _statisticsPlaybackState = PlaybackState.Stopped;
                InvokePrivate("HandlePlaybackStatisticsStateChanged", PlaybackState.Stopped);
                Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Null);
                Assert.That(_statistics.SplitDurations.Count, Is.EqualTo(1));
                Assert.That(_statistics.SplitDurations[0], Is.GreaterThan(0));
                Assert.That(_statistics.ApplyCount, Is.EqualTo(0));
                return 1;
            });

            await _playback.FlushPlaybackStatisticsAsync();
            Assert.That(_statistics.ApplyCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CaptureCheckpoint_StartsReplacementBeforeDrainAndCountsPreparationTime()
        {
            _statisticsPlaybackState = PlaybackState.Playing;
            _statisticsCurrentTrack = new MusicTrack { Id = 42, FileName = "playing.mp3", DurationMs = 1000 };
            InvokePrivate("HandlePlaybackStatisticsStateChanged", PlaybackState.Playing);
            await Task.Delay(40);
            var oldSegmentId = GetPrivateField<string>("_activeSegmentBaseEventId");

            _statistics.BlockApply = true;
            var captureTask = _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
            {
                Assert.That(_statistics.ApplyCount, Is.EqualTo(1));
                await Task.Delay(40);
                _statisticsPlaybackState = PlaybackState.Paused;
                InvokePrivate("HandlePlaybackStatisticsStateChanged", PlaybackState.Paused);
                Assert.That(_statistics.ApplyCount, Is.EqualTo(1));
                return 1;
            });

            await _statistics.ApplyStarted.Task;
            var replacementSegmentId = GetPrivateField<string>("_activeSegmentBaseEventId");
            Assert.That(replacementSegmentId, Is.Not.EqualTo(oldSegmentId));
            Assert.That(GetPrivateField<MusicTrack>("_activeSegmentTrack"), Is.Not.Null);

            var preparationStarted = Stopwatch.StartNew();
            await Task.Delay(150);
            var preparationDuration = preparationStarted.Elapsed;
            Assert.That(captureTask.IsCompleted, Is.False);

            _statistics.AllowApply.TrySetResult(true);
            await captureTask;

            Assert.That(_statistics.ApplyCount, Is.EqualTo(1));
            Assert.That(_statistics.SplitDurations.Count, Is.EqualTo(2));
            Assert.That(_statistics.SplitDurations[1], Is.GreaterThanOrEqualTo((long)preparationDuration.TotalMilliseconds - 40));
            await _playback.FlushPlaybackStatisticsAsync();
            Assert.That(_statistics.ApplyCount, Is.EqualTo(2));
        }

        [Test]
        public async Task CaptureCheckpoint_FailsFastForNestedMaintenanceOperations()
        {
            await _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
            {
                Assert.ThrowsAsync<InvalidOperationException>(() => _playback.CapturePlaybackStatisticsCheckpointAsync(() => Task.FromResult(1)));
                Assert.ThrowsAsync<InvalidOperationException>(() => _playback.FlushPlaybackStatisticsAsync());
                Assert.ThrowsAsync<InvalidOperationException>(() => _playback.ClearPlaybackStatisticsAsync());
                Assert.ThrowsAsync<InvalidOperationException>(() => _playback.RotateIfCurrentGenerationTombstonedAsync());
                Assert.ThrowsAsync<InvalidOperationException>(() => _playback.PersistPendingPlaybackStatisticsAsync());
                await Task.CompletedTask;
                return 1;
            });
        }

        [Test]
        public async Task PersistPendingPlaybackStatistics_WaitsUntilCaptureExits()
        {
            var captureEntered = new TaskCompletionSource<bool>();
            var releaseCapture = new TaskCompletionSource<bool>();
            var captureTask = _playback.CapturePlaybackStatisticsCheckpointAsync(async () =>
            {
                captureEntered.SetResult(true);
                await releaseCapture.Task;
                return 1;
            });

            await captureEntered.Task;
            var persistTask = _playback.PersistPendingPlaybackStatisticsAsync();
            await Task.Delay(50);
            Assert.That(persistTask.IsCompleted, Is.False);

            releaseCapture.SetResult(true);
            await captureTask;
            await persistTask;
        }

        private void AddPendingSettlement()
        {
            AddPendingSettlement("checkpoint-failure");
        }

        private void AddPendingSettlement(string id)
        {
            var field = typeof(PlaybackService).GetField("_pendingPlaybackSettlements", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var pending = (List<PlaybackStatisticsSettlement>)field.GetValue(_playback);
            pending.Add(new PlaybackStatisticsSettlement
            {
                SettlementEventId = id,
                FileName = "pending.mp3",
                NormalizedFileName = "pending.mp3",
                TrackDurationMs = 1000,
                DeviceId = "device",
                Generation = 0,
                StartedAtUtcMs = 1,
                DurationMs = 100,
                SourceLocalDate = "2026-07-10",
                AppliedAtUtcMs = 2,
                SourceKind = "test"
            });
        }

        private IReadOnlyList<PlaybackStatisticsSettlement> PendingSettlements()
        {
            var field = typeof(PlaybackService).GetField("_pendingPlaybackSettlements", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return ((List<PlaybackStatisticsSettlement>)field.GetValue(_playback)).ToArray();
        }

        private T GetPrivateField<T>(string name)
        {
            var field = typeof(PlaybackService).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (T)field.GetValue(_playback);
        }

        private void InvokePrivate(string name, params object[] arguments)
        {
            var method = typeof(PlaybackService).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(_playback, arguments);
        }

        private sealed class FailingPlaybackStatisticsLocalService : IPlaybackStatisticsLocalService
        {
            public bool FailApply { get; set; }
            public int ApplyCount { get; private set; }
            public bool BlockClear { get; set; }
            public bool BlockObserve { get; set; }
            public bool BlockApply { get; set; }
            public TaskCompletionSource<bool> ClearStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> AllowClear { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> ObserveStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> AllowObserve { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> ApplyStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> AllowApply { get; } = new TaskCompletionSource<bool>();
            public List<long> SplitDurations { get; } = new List<long>();

            public PlaybackStatisticsRecordingContext GetRecordingContext() => new PlaybackStatisticsRecordingContext { DeviceId = "device", CurrentGeneration = 0 };
            public IReadOnlyList<PlaybackStatisticsSettlement> Split(DateTimeOffset sourceLocalStart, long startedAtUtcMs, long durationMs, PlaybackStatisticsSettlement template)
            {
                SplitDurations.Add(durationMs);
                template.DurationMs = durationMs;
                return new[] { template };
            }
            public Task<bool> ApplyAsync(PlaybackStatisticsSettlement settlement)
            {
                if (FailApply) throw new InvalidOperationException("settlement apply failed");
                ApplyStarted.TrySetResult(true);
                if (BlockApply) return ApplyAfterBlockAsync();
                ApplyCount++;
                return Task.FromResult(true);
            }
            private async Task<bool> ApplyAfterBlockAsync()
            {
                await AllowApply.Task;
                ApplyCount++;
                return true;
            }
            public async Task<PlaybackStatisticsGenerationClearResult> ClearCurrentGenerationAsync()
            {
                ClearStarted.TrySetResult(true);
                if (BlockClear) await AllowClear.Task;
                return new PlaybackStatisticsGenerationClearResult { OldGeneration = 0, NewGeneration = 1 };
            }
            public async Task<PlaybackStatisticsTombstoneObservationResult> ObserveCurrentGenerationTombstoneAsync()
            {
                ObserveStarted.TrySetResult(true);
                if (BlockObserve) await AllowObserve.Task;
                return new PlaybackStatisticsTombstoneObservationResult();
            }
            public Task<int> RelinkSongsAsync() => Task.FromResult(0);
            public Task<IReadOnlyList<PlaybackStatisticsSourceDevice>> GetSourceDevicesAsync() => Task.FromResult<IReadOnlyList<PlaybackStatisticsSourceDevice>>(new List<PlaybackStatisticsSourceDevice>());
            public Task RenameDeviceAsync(string deviceId, string displayName, long updatedAtUtcMs) => Task.CompletedTask;
            public Task<int> TombstoneKnownActiveGenerationsAsync(IEnumerable<string> deviceIds, long tombstonedAtUtcMs, string actorDeviceId, string reason) => Task.FromResult(0);
            public Task<int> TombstoneAllKnownNonLocalGenerationsAsync(long tombstonedAtUtcMs, string actorDeviceId, string reason) => Task.FromResult(0);
            public Task<List<PlaybackStatisticItem>> GetTopTracksAsync(PlaybackStatisticsPeriod period, int limit = 5, DateTimeOffset? viewerLocalNow = null) => Task.FromResult(new List<PlaybackStatisticItem>());
        }
    }
}
