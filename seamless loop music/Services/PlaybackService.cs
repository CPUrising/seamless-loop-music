using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Prism.Events;
using Prism.Mvvm;
using Unity;
using seamless_loop_music.Models;
using seamless_loop_music.Events;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Services;
using Microsoft.Win32;

namespace seamless_loop_music.Services
{
    public class PlaybackService : IPlaybackService
    {
        private readonly AudioLooper _audioLooper;
        private readonly ITrackRepository _trackRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaylistManager _playlistManager;
        private readonly IQueueManager _queueManager;
        private readonly TrackMetadataService _metadataService;
        private readonly IPlaybackStatisticsLocalService _playbackStatisticsLocalService;
        private readonly PlaybackStatisticsOutbox _playbackStatisticsOutbox;
        private readonly System.Threading.SemaphoreSlim _loadLock = new System.Threading.SemaphoreSlim(1, 1);
        private readonly System.Threading.SemaphoreSlim _playbackStatisticsMaintenanceLock = new System.Threading.SemaphoreSlim(1, 1);
        private readonly System.Threading.AsyncLocal<int> _captureDelegateDepth = new System.Threading.AsyncLocal<int>();
        private readonly object _playbackStatisticsLock = new object();
        private readonly List<PlaybackStatisticsSettlement> _pendingPlaybackSettlements = new List<PlaybackStatisticsSettlement>();
        private Task _playbackStatisticsWriteChain = Task.CompletedTask;
        private System.Threading.Timer _playbackCheckpointTimer;
        private MusicTrack _activeSegmentTrack;
        private PlaybackStatisticsRecordingContext _activeSegmentContext;
        private DateTimeOffset _activeSegmentSourceLocalStart;
        private string _activeSegmentBaseEventId;
        private long _activeSegmentStartedAtUtcMs;
        private long _activeSegmentStopwatchTimestamp;
        private bool _suppressStatisticsStateChanges;
        private bool _statisticsFlushing;
        private bool _statisticsClearing;
        private bool _statisticsCaptureFenced;
        private HashSet<string> _captureDrainSettlementIds;
        private readonly Func<PlaybackState> _statisticsPlaybackStateProvider;
        private readonly Func<MusicTrack> _statisticsCurrentTrackProvider;

        public MusicTrack CurrentTrack { get; private set; }
        public CategoryItem CurrentCategory { get; private set; }
        public PlaybackState PlaybackState => _audioLooper?.PlaybackState ?? PlaybackState.Stopped;
        public TimeSpan CurrentTime => _audioLooper?.CurrentTime ?? TimeSpan.Zero;
        public long CurrentSample => _audioLooper != null ? (long)(_audioLooper.CurrentTime.TotalSeconds * _audioLooper.SampleRate) : 0;
        public TimeSpan TotalTime => _audioLooper?.TotalTime ?? TimeSpan.Zero;
        public int SampleRate => _audioLooper?.SampleRate ?? 44100;
        public bool IsABFusionLoaded => _audioLooper?.IsABFusionLoaded ?? false;
        public float Volume 
        { 
            get => _audioLooper.Volume; 
            set 
            { 
                if (_audioLooper.Volume != value)
                {
                    _audioLooper.Volume = value; 
                    VolumeChanged?.Invoke(value);
                }
            } 
        }
        public event Action<float> VolumeChanged;
        public bool IsSeamlessLoopEnabled 
        { 
            get => _audioLooper.IsSeamlessLoopEnabled; 
            set 
            { 
                if (_audioLooper.IsSeamlessLoopEnabled != value)
                {
                    _audioLooper.IsSeamlessLoopEnabled = value; 
                    SeamlessLoopChanged?.Invoke(value);
                }
            } 
        }
        public event Action<bool> SeamlessLoopChanged;

        public bool IsFeatureLoopEnabled 
        { 
            get => _audioLooper.IsFeatureLoopEnabled; 
            set 
            { 
                if (_audioLooper.IsFeatureLoopEnabled != value)
                {
                    _audioLooper.IsFeatureLoopEnabled = value; 
                    FeatureLoopChanged?.Invoke(value);
                }
            } 
        }
        public event Action<bool> FeatureLoopChanged;
        
        public double MatchWindowSize { get => _audioLooper.MatchWindowSize; set => _audioLooper.MatchWindowSize = value; }
        public double MatchSearchRadius { get => _audioLooper.MatchSearchRadius; set => _audioLooper.MatchSearchRadius = value; }

        public IReadOnlyList<MusicTrack> Queue => _queueManager.Queue;
        public int CurrentIndex => _queueManager.CurrentIndex;
        public PlayMode PlayMode 
        { 
            get => _queueManager.PlayMode; 
            set 
            { 
                if (_queueManager.PlayMode != value)
                {
                    _queueManager.PlayMode = value; 
                    PlayModeChanged?.Invoke(value);
                }
            } 
        }
        public event Action<PlayMode> PlayModeChanged;

        public event Action<MusicTrack> TrackChanged;
        public event Action<PlaybackState> StateChanged;
        public event Action QueueChanged;

        [InjectionConstructor]
        public PlaybackService(ITrackRepository trackRepository, IPlaylistManager playlistManager, IEventAggregator eventAggregator, IQueueManager queueManager, TrackMetadataService metadataService, IPlaybackStatisticsLocalService playbackStatisticsLocalService)
            : this(trackRepository, playlistManager, eventAggregator, queueManager, metadataService, playbackStatisticsLocalService, null, null)
        {
        }

        public PlaybackService(ITrackRepository trackRepository, IPlaylistManager playlistManager, IEventAggregator eventAggregator, IQueueManager queueManager, TrackMetadataService metadataService, IPlaybackStatisticsLocalService playbackStatisticsLocalService, Func<PlaybackState> statisticsPlaybackStateProvider = null, Func<MusicTrack> statisticsCurrentTrackProvider = null)
        {
            _trackRepository = trackRepository;
            _playlistManager = playlistManager;
            _eventAggregator = eventAggregator;
            _queueManager = queueManager;
            _metadataService = metadataService;
            _playbackStatisticsLocalService = playbackStatisticsLocalService;
            _playbackStatisticsOutbox = new PlaybackStatisticsOutbox();
            _audioLooper = new AudioLooper();
            _statisticsPlaybackStateProvider = statisticsPlaybackStateProvider ?? (() => _audioLooper.PlaybackState);
            _statisticsCurrentTrackProvider = statisticsCurrentTrackProvider ?? (() => CurrentTrack);

            foreach (var settlement in _playbackStatisticsOutbox.LoadState().SettlementEvents)
                if (!_pendingPlaybackSettlements.Any(x => x.SettlementEventId == settlement.SettlementEventId)) _pendingPlaybackSettlements.Add(settlement);

            _audioLooper.OnPlayStateChanged += state =>
            {
                HandlePlaybackStatisticsStateChanged(state);
                _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Publish(state);
                StateChanged?.Invoke(state);
            };
            
            _audioLooper.OnStatusChanged += msg =>
            {
                _eventAggregator.GetEvent<StatusMessageEvent>().Publish(msg);
            };

            _queueManager.QueueChanged += () =>
            {
                QueueChanged?.Invoke();
            };

            _audioLooper.OnTrackEnded += OnTrackEnded;
            _audioLooper.OnPlaybackError += HandlePlaybackError;
            SystemEvents.TimeChanged += OnSystemTimeChanged;
            _playbackCheckpointTimer = new System.Threading.Timer(_ => CheckpointPlaybackStatistics(), null, 30000, 30000);
            if (_pendingPlaybackSettlements.Count > 0) QueuePlaybackStatisticsWrite();
        }

        private void HandlePlaybackStatisticsStateChanged(PlaybackState state)
        {
            var shouldQueue = false;
            lock (_playbackStatisticsLock)
            {
                if (_suppressStatisticsStateChanges) return;
                if (_statisticsClearing) return;
                RotatePlaybackSegmentForOffsetChangeLocked();
                if (state == PlaybackState.Playing) StartPlaybackSegmentLocked();
                else { ClosePlaybackSegmentLocked(); shouldQueue = true; }
            }
            if (shouldQueue) QueuePlaybackStatisticsWrite();
        }

        private void HandlePlaybackError(Exception exception)
        {
            lock (_playbackStatisticsLock) ClosePlaybackSegmentLocked();
            System.Diagnostics.Debug.WriteLine($"[Playback statistics] Playback error closed active segment: {exception?.Message}");
            QueuePlaybackStatisticsWrite();
        }

        private void StartPlaybackSegmentLocked()
        {
            var currentTrack = _statisticsCurrentTrackProvider();
            if (_statisticsPlaybackStateProvider() != PlaybackState.Playing || _activeSegmentTrack != null || currentTrack == null || currentTrack.Id <= 0) return;
            _activeSegmentTrack = new MusicTrack { Id = currentTrack.Id, FileName = currentTrack.FileName, DurationMs = currentTrack.DurationMs, TotalSamples = currentTrack.TotalSamples };
            _activeSegmentContext = _playbackStatisticsLocalService.GetRecordingContext();
            _activeSegmentSourceLocalStart = DateTimeOffset.Now;
            _activeSegmentBaseEventId = Guid.NewGuid().ToString("N");
            _activeSegmentStartedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _activeSegmentStopwatchTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        private void ClosePlaybackSegmentLocked()
        {
            if (_activeSegmentTrack == null) return;
            var durationMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - _activeSegmentStopwatchTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (durationMs > 0)
            {
                var track = _activeSegmentTrack;
                var template = new PlaybackStatisticsSettlement { SettlementEventId = _activeSegmentBaseEventId, FileName = track.FileName, NormalizedFileName = Sync.SyncSnapshotSerializer.NormalizePlaybackSongFileName(track.FileName), TrackDurationMs = track.DurationMs, TotalSamples = track.TotalSamples, LocalTrackId = track.Id, DeviceId = _activeSegmentContext.DeviceId, Generation = _activeSegmentContext.CurrentGeneration, AppliedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), SourceKind = "live" };
                _pendingPlaybackSettlements.AddRange(_playbackStatisticsLocalService.Split(_activeSegmentSourceLocalStart, _activeSegmentStartedAtUtcMs, durationMs, template));
            }
            _activeSegmentTrack = null;
        }

        private void CheckpointPlaybackStatistics()
        {
            lock (_playbackStatisticsLock)
            {
                if (_statisticsFlushing) return;
                RotatePlaybackSegmentForOffsetChangeLocked();
                if (_activeSegmentTrack != null) { ClosePlaybackSegmentLocked(); StartPlaybackSegmentLocked(); }
            }
            QueuePlaybackStatisticsWrite();
        }

        private void RotatePlaybackSegmentForOffsetChangeLocked()
        {
            if (_activeSegmentTrack == null || _statisticsPlaybackStateProvider() != PlaybackState.Playing) return;
            if (!PlaybackStatisticsOffsetHelper.HasOffsetChanged(_activeSegmentSourceLocalStart, DateTimeOffset.Now.Offset)) return;
            ClosePlaybackSegmentLocked();
            StartPlaybackSegmentLocked();
        }

        private void QueuePlaybackStatisticsWrite()
        {
            lock (_playbackStatisticsLock)
            {
                if (_statisticsClearing || _statisticsCaptureFenced) return;
                _playbackStatisticsWriteChain = _playbackStatisticsWriteChain.ContinueWith(_ => WritePendingPlaybackSegmentsAsync()).Unwrap();
            }
        }

        private async Task WritePendingPlaybackSegmentsAsync(bool allowCaptureFencedSettlements = false, ISet<string> settlementIds = null)
        {
            PlaybackStatisticsSettlement[] settlements;
            lock (_playbackStatisticsLock)
            {
                if (_statisticsCaptureFenced && !allowCaptureFencedSettlements) return;
                settlements = settlementIds == null
                    ? _pendingPlaybackSettlements.ToArray()
                    : _pendingPlaybackSettlements.Where(x => settlementIds.Contains(x.SettlementEventId)).ToArray();
            }
            await _playbackStatisticsOutbox.SaveSettlementEventsAsync(settlements);
            foreach (var settlement in settlements)
            {
                try
                {
                    await _playbackStatisticsLocalService.ApplyAsync(settlement);
                    lock (_playbackStatisticsLock) _pendingPlaybackSettlements.RemoveAll(x => x.SettlementEventId == settlement.SettlementEventId);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Playback statistics] {ex.Message}"); }
            }
            lock (_playbackStatisticsLock)
            {
                if (_statisticsCaptureFenced && !allowCaptureFencedSettlements) return;
                settlements = settlementIds == null
                    ? _pendingPlaybackSettlements.ToArray()
                    : _pendingPlaybackSettlements.Where(x => settlementIds.Contains(x.SettlementEventId)).ToArray();
            }
            try { await _playbackStatisticsOutbox.SaveSettlementEventsAsync(settlements); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Playback statistics] Could not persist outbox: {ex.Message}"); }
        }

        private void OnTrackEnded()
        {
            // 必须在 UI 线程执行，因为 NAudio 回调在后台线程触发
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PlayMode == PlayMode.SingleLoop)
                {
                    SeekToSample(0);
                    Play();
                }
                else
                {
                    Next();
                }
            }));
        }

        public async Task LoadTrackAsync(MusicTrack track, bool autoPlay = false)
        {
            if (track == null) return;

            await _loadLock.WaitAsync();
            try
            {
                // 如果曲目没变且已经在播放，则不需要重新加载音频流，避免打断
                bool isAlreadyLoaded = CurrentTrack != null && CurrentTrack.Id == track.Id;
                
                if (!isAlreadyLoaded)
                {
                    lock (_playbackStatisticsLock) ClosePlaybackSegmentLocked();
                    QueuePlaybackStatisticsWrite();
                }
                CurrentTrack = track;

                if (!isAlreadyLoaded)
                {
                    string partB = _metadataService.FindPartB(track.FilePath);
                    await Task.Run(() => _audioLooper.LoadAudio(track.FilePath, partB));

                    // 核心逻辑：A/B 融合模式下，始终以 AudioLooper 从物理文件计算的衔接点为准，
                    // 避免数据库旧值（可能由 TagLib 估算）与实际解码长度不一致
                    if (_audioLooper.IsABFusionLoaded)
                    {
                        // 始终更新物理总采样数，确保 UI 和边界判定准确
                        track.TotalSamples = _audioLooper.TotalSamples;

                        // 如果数据库里已经有了循环点设置（LoopEnd > 0），则尊重用户的设置，但要确保不越界
                        if (track.LoopEnd > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AB-Load] Using DB values: {track.LoopStart} - {track.LoopEnd} (Total: {track.TotalSamples})");
                            track.LoopStart = Math.Min(track.LoopStart, track.TotalSamples);
                            track.LoopEnd = Math.Min(track.LoopEnd, track.TotalSamples);
                            _audioLooper.SetLoopPoints(track.LoopStart, track.LoopEnd);
                        }
                        else
                        {
                            // 第一次加载：使用物理衔接点作为默认值
                            System.Diagnostics.Debug.WriteLine($"[AB-Load] First time, using Seam: {_audioLooper.LoopStartSample} - {_audioLooper.LoopEndSample}");
                            track.LoopStart = _audioLooper.LoopStartSample;
                            track.LoopEnd = _audioLooper.LoopEndSample;
                            _trackRepository.UpdateLoopPoints(track.Id, track.LoopStart, track.LoopEnd);
                        }
                    }
                    else
                    {
                        _audioLooper.SetLoopStartSample(track.LoopStart);
                        _audioLooper.SetLoopEndSample(track.LoopEnd);
                    }
                }

                TrackChanged?.Invoke(track);
                _eventAggregator.GetEvent<TrackLoadedEvent>().Publish(track);

                if (autoPlay)
                {
                    Play();
                }
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public void Play() => _audioLooper.Play();
        public void Pause() => _audioLooper.Pause();
        public void Stop()
        {
            _audioLooper.Stop();
            lock (_playbackStatisticsLock) ClosePlaybackSegmentLocked();
            QueuePlaybackStatisticsWrite();
        }
        
        public async void Next()
        {
            try
            {
                var next = _queueManager.GetNextTrack();
                if (next != null) await LoadTrackAsync(next, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Next失败] {ex.Message}");
            }
        }

        public async void Previous()
        {
            try
            {
                var prev = _queueManager.GetPreviousTrack();
                if (prev != null) await LoadTrackAsync(prev, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Previous失败] {ex.Message}");
            }
        }

        public void Seek(TimeSpan position)
        {
            lock (_playbackStatisticsLock) _suppressStatisticsStateChanges = true;
            try { _audioLooper.Seek(position.TotalSeconds / TotalTime.TotalSeconds); }
            finally { AlignPlaybackStatisticsAfterSeek(); }
        }
        public void SeekToSample(long sample)
        {
            lock (_playbackStatisticsLock) _suppressStatisticsStateChanges = true;
            try { _audioLooper.SeekToSample(sample); }
            finally { AlignPlaybackStatisticsAfterSeek(); }
        }

        private void AlignPlaybackStatisticsAfterSeek()
        {
            lock (_playbackStatisticsLock)
            {
                _suppressStatisticsStateChanges = false;
                RotatePlaybackSegmentForOffsetChangeLocked();
                if (_statisticsPlaybackStateProvider() == PlaybackState.Playing) StartPlaybackSegmentLocked(); else ClosePlaybackSegmentLocked();
            }
        }

        public void SetLoopPoints(long startSample, long endSample)
        {
            _audioLooper.SetLoopPoints(startSample, endSample);
            
            if (CurrentTrack != null)
            {
                CurrentTrack.LoopStart = startSample;
                CurrentTrack.LoopEnd = endSample;
                _trackRepository.UpdateLoopPoints(CurrentTrack.Id, startSample, endSample);
            }
            
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Publish((startSample, endSample));
        }

        public void ResetABLoopPoints()
        {
            System.Diagnostics.Debug.WriteLine($"[AB-Reset] BEFORE: LoopStart={_audioLooper.LoopStartSample}, LoopEnd={_audioLooper.LoopEndSample}");
            _audioLooper.ResetABLoopPoints();
            System.Diagnostics.Debug.WriteLine($"[AB-Reset] AFTER: LoopStart={_audioLooper.LoopStartSample}, LoopEnd={_audioLooper.LoopEndSample}");
            if (CurrentTrack != null)
            {
                CurrentTrack.LoopStart = _audioLooper.LoopStartSample;
                CurrentTrack.LoopEnd = _audioLooper.LoopEndSample;
                _trackRepository.UpdateLoopPoints(CurrentTrack.Id, CurrentTrack.LoopStart, CurrentTrack.LoopEnd);
            }
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Publish((_audioLooper.LoopStartSample, _audioLooper.LoopEndSample));
        }

        public async Task<(long Start, long End)> FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart)
        {
            var tcs = new TaskCompletionSource<(long, long)>();
            _audioLooper.FindBestLoopPointsAsync(currentStart, currentEnd, adjustStart, (start, end) => 
            {
                tcs.SetResult((start, end));
            });
            return await tcs.Task;
        }

        public async Task EnqueueArtistAsync(string artistName)
        {
            var tracks = await _trackRepository.GetByArtistAsync(artistName);
            if (tracks != null && tracks.Any())
            {
                CurrentCategory = new CategoryItem { Name = artistName, Type = CategoryType.Artist };
                _queueManager.SetQueue(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public async Task EnqueueAlbumAsync(string albumName)
        {
            var tracks = await _trackRepository.GetByAlbumAsync(albumName);
            if (tracks != null && tracks.Any())
            {
                CurrentCategory = new CategoryItem { Name = albumName, Type = CategoryType.Album };
                _queueManager.SetQueue(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public async Task EnqueuePlaylistAsync(CategoryItem playlistItem)
        {
            List<MusicTrack> tracks = null;
            if (playlistItem.Id == -1)
            {
                tracks = await _trackRepository.GetAllAsync();
            }
            else if (playlistItem.Id == -2)
            {
                var all = await _trackRepository.GetAllAsync();
                tracks = all.Where(t => t.Rating > 0).OrderByDescending(t => t.Rating).ToList();
            }
            else if (playlistItem.Id > 0)
            {
                tracks = await _playlistManager.GetTracksInPlaylistAsync(playlistItem.Id);
            }

            if (tracks != null && tracks.Any())
            {
                CurrentCategory = playlistItem;
                _queueManager.SetQueue(tracks, tracks.First());
                await LoadTrackAsync(tracks.First(), true);
            }
        }

        public void SetQueue(IEnumerable<MusicTrack> tracks, MusicTrack currentTrack = null, CategoryItem category = null)
        {
            CurrentCategory = category;
            _queueManager.SetQueue(tracks, currentTrack);
        }

        public void AddToQueue(MusicTrack track)
        {
            _queueManager.AddToQueue(track);
        }

        public void RemoveFromQueue(int index)
        {
            _queueManager.RemoveFromQueue(index);
        }

        public void ClearQueue()
        {
            _queueManager.ClearQueue();
        }

        public void MoveQueueItem(int fromIndex, int toIndex)
        {
            _queueManager.MoveTo(fromIndex, toIndex);
        }

        public async Task FlushPlaybackStatisticsAsync()
        {
            ThrowIfCaptureDelegateReentry();
            await _playbackStatisticsMaintenanceLock.WaitAsync().ConfigureAwait(false);
            try
            {
            _ = BeginPlaybackStatisticsLifecycle(false);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                QueuePlaybackStatisticsWrite();
                Task chain;
                lock (_playbackStatisticsLock) chain = _playbackStatisticsWriteChain;
                await chain;

                lock (_playbackStatisticsLock)
                {
                    if (_pendingPlaybackSettlements.Count == 0) return;
                }

                await Task.Delay(100);
            }

            await PersistPendingPlaybackStatisticsUngatedAsync();
            }
            finally
            {
                _playbackStatisticsMaintenanceLock.Release();
            }
        }

        public async Task<T> CapturePlaybackStatisticsCheckpointAsync<T>(Func<Task<T>> captureAsync)
        {
            if (captureAsync == null) throw new ArgumentNullException(nameof(captureAsync));
            ThrowIfCaptureDelegateReentry();

            await _playbackStatisticsMaintenanceLock.WaitAsync().ConfigureAwait(false);
            var delegateStarted = false;
            try
            {
                var existingWriteChain = BeginPlaybackStatisticsLifecycle(false, startCaptureReplacement: true);
                HashSet<string> captureDrainSettlementIds;
                lock (_playbackStatisticsLock)
                    captureDrainSettlementIds = new HashSet<string>(_captureDrainSettlementIds);

                await existingWriteChain.ConfigureAwait(false);
                await QueueAndAwaitPlaybackStatisticsWriteAsync(captureDrainSettlementIds).ConfigureAwait(false);
                await DrainPendingPlaybackStatisticsForCheckpointAsync(captureDrainSettlementIds).ConfigureAwait(false);

                _captureDelegateDepth.Value++;
                delegateStarted = true;
                return await captureAsync().ConfigureAwait(false);
            }
            finally
            {
                if (delegateStarted) _captureDelegateDepth.Value--;
                try
                {
                    RestorePlaybackStatisticsLifecycle();
                }
                finally
                {
                    _playbackStatisticsMaintenanceLock.Release();
                }
            }
        }

        private Task BeginPlaybackStatisticsLifecycle(bool clearing, bool startCaptureReplacement = false)
        {
            _playbackCheckpointTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            lock (_playbackStatisticsLock)
            {
                _statisticsClearing = clearing;
                _statisticsFlushing = true;
                ClosePlaybackSegmentLocked();
                if (startCaptureReplacement)
                {
                    _statisticsCaptureFenced = true;
                    _captureDrainSettlementIds = new HashSet<string>(_pendingPlaybackSettlements.Select(x => x.SettlementEventId));
                    StartPlaybackSegmentLocked();
                }
                return _playbackStatisticsWriteChain;
            }
        }

        private async Task QueueAndAwaitPlaybackStatisticsWriteAsync(ISet<string> settlementIds)
        {
            Task writeChain;
            lock (_playbackStatisticsLock)
            {
                _playbackStatisticsWriteChain = _playbackStatisticsWriteChain
                    .ContinueWith(_ => WritePendingPlaybackSegmentsAsync(true, settlementIds)).Unwrap();
                writeChain = _playbackStatisticsWriteChain;
            }
            await writeChain.ConfigureAwait(false);
        }

        private void RestorePlaybackStatisticsLifecycle()
        {
            bool hasPending;
            lock (_playbackStatisticsLock)
            {
                _statisticsCaptureFenced = false;
                _captureDrainSettlementIds = null;
                _statisticsClearing = false;
                _statisticsFlushing = false;
                var currentTrack = _statisticsCurrentTrackProvider();
                var isPlaying = _statisticsPlaybackStateProvider() == PlaybackState.Playing;
                if (_activeSegmentTrack != null && (!isPlaying || currentTrack == null || currentTrack.Id <= 0 || _activeSegmentTrack.Id != currentTrack.Id))
                    ClosePlaybackSegmentLocked();
                if (isPlaying && currentTrack != null && currentTrack.Id > 0)
                    StartPlaybackSegmentLocked();
                hasPending = _pendingPlaybackSettlements.Count > 0;
            }
            _playbackCheckpointTimer?.Change(30000, 30000);
            if (hasPending) QueuePlaybackStatisticsWrite();
        }

        private async Task DrainPendingPlaybackStatisticsForCheckpointAsync(ISet<string> settlementIds)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                PlaybackStatisticsSettlement[] settlements;
                lock (_playbackStatisticsLock)
                    settlements = _pendingPlaybackSettlements.Where(x => settlementIds.Contains(x.SettlementEventId)).ToArray();
                if (settlements.Length == 0) return;

                try { await _playbackStatisticsOutbox.SaveSettlementEventsAsync(settlements).ConfigureAwait(false); }
                catch (Exception ex) { lastError = ex; }

                foreach (var settlement in settlements)
                {
                    try
                    {
                        await _playbackStatisticsLocalService.ApplyAsync(settlement).ConfigureAwait(false);
                        lock (_playbackStatisticsLock) _pendingPlaybackSettlements.RemoveAll(x => x.SettlementEventId == settlement.SettlementEventId);
                    }
                    catch (Exception ex) { lastError = ex; }
                }

                lock (_playbackStatisticsLock)
                    settlements = _pendingPlaybackSettlements.Where(x => settlementIds.Contains(x.SettlementEventId)).ToArray();
                try { await _playbackStatisticsOutbox.SaveSettlementEventsAsync(settlements).ConfigureAwait(false); }
                catch (Exception ex) { lastError = ex; }
                if (settlements.Length == 0) return;
                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Playback statistics settlements could not be persisted and applied before sync capture.", lastError);
        }

        public async Task PersistPendingPlaybackStatisticsAsync()
        {
            ThrowIfCaptureDelegateReentry();
            await _playbackStatisticsMaintenanceLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await PersistPendingPlaybackStatisticsUngatedAsync().ConfigureAwait(false);
            }
            finally
            {
                _playbackStatisticsMaintenanceLock.Release();
            }
        }

        private async Task PersistPendingPlaybackStatisticsUngatedAsync()
        {
            PlaybackStatisticsSettlement[] pending;
            lock (_playbackStatisticsLock) pending = _pendingPlaybackSettlements.ToArray();
            await _playbackStatisticsOutbox.SaveSettlementEventsAsync(pending).ConfigureAwait(false);
        }

        public void ResumePlaybackStatisticsAfterFailedFlush()
        {
            lock (_playbackStatisticsLock)
            {
                if (!_statisticsFlushing) return;
                _statisticsFlushing = false;
                if (_audioLooper.PlaybackState == PlaybackState.Playing && CurrentTrack != null && CurrentTrack.Id > 0)
                    StartPlaybackSegmentLocked();
            }
            _playbackCheckpointTimer?.Change(30000, 30000);
        }

        public async Task<int> ClearPlaybackStatisticsAsync()
        {
            ThrowIfCaptureDelegateReentry();
            await _playbackStatisticsMaintenanceLock.WaitAsync().ConfigureAwait(false);
            try
            {
            var existingWriteChain = BeginPlaybackStatisticsLifecycle(true);

            var succeeded = false;
            PlaybackStatisticsSettlement[] discarded = null;
            PlaybackStatisticsRecordingContext clearingContext = null;
            try
            {
                await existingWriteChain;
                clearingContext = _playbackStatisticsLocalService.GetRecordingContext();
                var clearResult = await _playbackStatisticsLocalService.ClearCurrentGenerationAsync();
                lock (_playbackStatisticsLock)
                {
                    discarded = _pendingPlaybackSettlements
                        .Where(x => x.DeviceId == clearingContext.DeviceId && x.Generation == clearResult.OldGeneration)
                        .ToArray();
                    _pendingPlaybackSettlements.RemoveAll(x => x.DeviceId == clearingContext.DeviceId && x.Generation == clearResult.OldGeneration);
                }
                PlaybackStatisticsSettlement[] retained;
                lock (_playbackStatisticsLock) retained = _pendingPlaybackSettlements.ToArray();
                await _playbackStatisticsOutbox.SaveSettlementEventsAsync(retained);
                lock (_playbackStatisticsLock)
                {
                    _statisticsClearing = false;
                    _statisticsFlushing = false;
                    if (_audioLooper.PlaybackState == PlaybackState.Playing && CurrentTrack != null && CurrentTrack.Id > 0)
                        StartPlaybackSegmentLocked();
                }
                succeeded = true;
                return clearResult.AffectedContributionCount;
            }
            finally
            {
                bool hasPending;
                lock (_playbackStatisticsLock)
                {
                    if (!succeeded)
                    {
                        if (discarded != null) _pendingPlaybackSettlements.AddRange(discarded.Where(x => !_pendingPlaybackSettlements.Any(y => y.SettlementEventId == x.SettlementEventId)));
                        _statisticsClearing = false;
                        _statisticsFlushing = false;
                        if (_audioLooper.PlaybackState == PlaybackState.Playing && CurrentTrack != null && CurrentTrack.Id > 0)
                            StartPlaybackSegmentLocked();
                    }
                    hasPending = _pendingPlaybackSettlements.Count > 0;
                }
                _playbackCheckpointTimer?.Change(30000, 30000);
                if (!succeeded && hasPending) QueuePlaybackStatisticsWrite();
            }
            }
            finally
            {
                _playbackStatisticsMaintenanceLock.Release();
            }
        }

        public void Dispose()
        {
            try { FlushPlaybackStatisticsAsync().GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Playback statistics] {ex.Message}"); }
            _playbackCheckpointTimer?.Dispose();
            SystemEvents.TimeChanged -= OnSystemTimeChanged;
            _audioLooper?.Dispose();
        }

        public async Task<bool> RotateIfCurrentGenerationTombstonedAsync()
        {
            ThrowIfCaptureDelegateReentry();
            await _playbackStatisticsMaintenanceLock.WaitAsync().ConfigureAwait(false);
            try
            {
            var existingWriteChain = BeginPlaybackStatisticsLifecycle(true);

            var succeeded = false;
            try
            {
                await existingWriteChain;
                var result = await _playbackStatisticsLocalService.ObserveCurrentGenerationTombstoneAsync();
                lock (_playbackStatisticsLock)
                {
                    if (result.Rotated)
                    {
                        var retained = PlaybackStatisticsSettlementFilter.ExcludingGeneration(_pendingPlaybackSettlements, result.DeviceId, result.OldGeneration);
                        _pendingPlaybackSettlements.Clear();
                        _pendingPlaybackSettlements.AddRange(retained);
                    }
                    _statisticsClearing = false;
                    _statisticsFlushing = false;
                    if (_audioLooper.PlaybackState == PlaybackState.Playing && CurrentTrack != null && CurrentTrack.Id > 0)
                        StartPlaybackSegmentLocked();
                }
                await PersistPendingPlaybackStatisticsUngatedAsync();
                succeeded = true;
                return result.Rotated;
            }
            finally
            {
                bool hasPending;
                lock (_playbackStatisticsLock)
                {
                    if (!succeeded)
                    {
                        _statisticsClearing = false;
                        _statisticsFlushing = false;
                        if (_audioLooper.PlaybackState == PlaybackState.Playing && CurrentTrack != null && CurrentTrack.Id > 0)
                            StartPlaybackSegmentLocked();
                    }
                    hasPending = _pendingPlaybackSettlements.Count > 0;
                }
                _playbackCheckpointTimer?.Change(30000, 30000);
                if (!succeeded && hasPending) QueuePlaybackStatisticsWrite();
            }
            }
            finally
            {
                _playbackStatisticsMaintenanceLock.Release();
            }
        }

        private void OnSystemTimeChanged(object sender, EventArgs e) { CheckpointPlaybackStatistics(); }

        private void ThrowIfCaptureDelegateReentry()
        {
            if (_captureDelegateDepth.Value > 0)
                throw new InvalidOperationException("Playback statistics maintenance cannot be re-entered from a capture delegate.");
        }
    }
}
