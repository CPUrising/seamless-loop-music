using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Flac;
using NAudio.Wave;
using TagLib;
using seamless_loop_music.Models;
using seamless_loop_music.Data;
using Prism.Events;
using seamless_loop_music.Events;

namespace seamless_loop_music.Services
{
    public class PlayerService : IPlayerService
    {
        private readonly IDatabaseHelper _databaseHelper;
        private readonly IPlaybackService _playbackService;
        private readonly ILoopAnalysisService _loopAnalysisService;
        private readonly TrackMetadataService _metadataService;
        private readonly IPlaylistManagerService _playlistManager;
        private readonly Lazy<IFolderWatcherService> _folderWatcherService; // 注入监视器服务
        private readonly IEventAggregator _eventAggregator; // 🌟 注入事件聚合器
        private readonly System.Threading.SemaphoreSlim _scanLock = new System.Threading.SemaphoreSlim(1, 1); // 并发扫描锁

        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        public int CurrentIndex { get; set; } = -1;
        public MusicTrack CurrentTrack => _playbackService.CurrentTrack;
        public PlaybackState PlaybackState => _playbackService.PlaybackState;
        
        public long LoopStartSample { get => CurrentTrack?.LoopStart ?? 0; set => _playbackService.SetLoopPoints(value, LoopEndSample); }
        public long LoopEndSample { get => CurrentTrack?.LoopEnd ?? 0; set => _playbackService.SetLoopPoints(LoopStartSample, value); }
        public int SampleRate => _playbackService.SampleRate;

        public float Volume { get => _playbackService.Volume; set => _playbackService.Volume = value; }
        public bool IsSeamlessLoopEnabled { get => _playbackService.IsSeamlessLoopEnabled; set => _playbackService.IsSeamlessLoopEnabled = value; }
        public bool IsFeatureLoopEnabled { get => _playbackService.IsFeatureLoopEnabled; set => _playbackService.IsFeatureLoopEnabled = value; }
        public double MatchWindowSize { get; set; } = 1.0;
        public double MatchSearchRadius { get; set; } = 5.0;

        public PlayerService(
            IDatabaseHelper databaseHelper, 
            IPlaybackService playbackService, 
            ILoopAnalysisService loopAnalysisService, 
            TrackMetadataService metadataService, 
            IPlaylistManagerService playlistManager, 
            Lazy<IFolderWatcherService> folderWatcherService,
            IEventAggregator eventAggregator)
        {
            _databaseHelper = databaseHelper;
            _playbackService = playbackService;
            _loopAnalysisService = loopAnalysisService;
            _metadataService = metadataService;
            _playlistManager = playlistManager;
            _folderWatcherService = folderWatcherService;
            _eventAggregator = eventAggregator;
        }

        public void Play() => _playbackService.Play();
        public void Pause() => _playbackService.Pause();
        public void Stop() => _playbackService.Stop();
        public void Seek(double percent) => _playbackService.Seek(TimeSpan.FromSeconds(percent * _playbackService.TotalTime.TotalSeconds));
        public void SeekToSample(long sample) => _playbackService.SeekToSample(sample);

        public List<Playlist> GetAllPlaylists() => _databaseHelper.GetAllPlaylists();
        public int CreatePlaylist(string name) => _databaseHelper.AddPlaylist(name);
        public void RenamePlaylist(int playlistId, string newName) => _databaseHelper.RenamePlaylist(playlistId, newName);
        public void DeletePlaylist(int playlistId) => _databaseHelper.DeletePlaylist(playlistId);

        public void AddTrackToPlaylist(int playlistId, MusicTrack track) => _databaseHelper.AddSongToPlaylist(playlistId, track.Id);
        public void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks) { foreach(var t in tracks) AddTrackToPlaylist(playlistId, t); }
        public void RemoveTrackFromPlaylist(int playlistId, int songId) => _databaseHelper.RemoveSongFromPlaylist(playlistId, songId);

        public void AddFilesToPlaylist(int playlistId, string[] filePaths) { }
        public List<MusicTrack> LoadPlaylistFromDb(int playlistId) => _databaseHelper.GetPlaylistTracks(playlistId).ToList();

        public void UpdatePlaylistsSortOrder(List<int> playlistIds) => _databaseHelper.UpdatePlaylistsSortOrder(playlistIds);
        public void UpdateTracksSortOrder(int playlistId, List<int> songIds) => _databaseHelper.UpdateTracksSortOrder(playlistId, songIds);

        public void SetLoopStart(long sample) => _playbackService.SetLoopPoints(sample, LoopEndSample);
        public void SetLoopEnd(long sample) => _playbackService.SetLoopPoints(LoopStartSample, sample);
        public void ApplyLoopCandidate(LoopCandidate candidate) { if (candidate != null) _playbackService.SetLoopPoints(candidate.LoopStart, candidate.LoopEnd); }
        public void ResetABLoopPoints() => _playbackService.ResetABLoopPoints();

        public async Task<List<LoopCandidate>> GetLoopCandidatesAsync()
        {
            if (CurrentTrack == null) return new List<LoopCandidate>();
            return await _loopAnalysisService.FetchTopLoopCandidatesAsync(CurrentTrack.FilePath);
        }

        public async Task<int> CheckAnalyzerStatusAsync()
        {
            return await _loopAnalysisService.CheckEnvironmentAsync();
        }

        public async Task UpdateTrackLoopCandidatesAsync(MusicTrack track, List<LoopCandidate> candidates)
        {
            if (track == null || candidates == null) return;
            
            // Serialize
            track.LoopCandidatesJson = _loopAnalysisService.SerializeLoopCandidates(candidates);
            
            // Update DB
            await Task.Run(() => _databaseHelper.UpdateTrackAnalysis(track));
        }

        public async Task AnalyzeTracksAsync(IEnumerable<MusicTrack> tracks, IProgress<(int current, int total, string fileName)> progress = null)
        {
            var trackList = tracks.ToList();
            int total = trackList.Count;
            int current = 0;

            foreach (var track in trackList)
            {
                current++;
                progress?.Report((current, total, track.FileName));

                try
                {
                    // 获取候选点
                    var candidates = await _loopAnalysisService.FetchTopLoopCandidatesAsync(track.FilePath);
                    if (candidates != null && candidates.Any())
                    {
                        // 序列化
                        track.LoopCandidatesJson = _loopAnalysisService.SerializeLoopCandidates(candidates);

                        // 如果当前没设置过循环点 (即 0 -> 总采样数)，则自动应用最佳点
                        if (track.LoopStart == 0 && track.LoopEnd == track.TotalSamples)
                        {
                            var best = candidates.First();
                            track.LoopStart = best.LoopStart;
                            track.LoopEnd = best.LoopEnd;
                        }

                        // 保存到数据库
                        await Task.Run(() => _databaseHelper.UpdateTrackAnalysis(track));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BatchAnalysis Error] {track.FileName}: {ex.Message}");
                }
            }
        }

        private static readonly string[] SupportedExtensions = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".wma", ".aiff", ".opus" };

        public List<string> GetMusicFolders() => _databaseHelper.GetMusicFolders();
        public void AddMusicFolder(string folderPath)
        {
            _databaseHelper.AddMusicFolder(folderPath);
            _folderWatcherService.Value.RefreshWatchers(); // 同步刷新监视器
        }
        public void RemoveMusicFolder(string folderPath)
        {
            _databaseHelper.RemoveMusicFolder(folderPath);
            _databaseHelper.CleanupTracksOutsideMusicFolders(_databaseHelper.GetMusicFolders());
            _databaseHelper.RepairMissingCategoryCovers();
            _folderWatcherService.Value.RefreshWatchers(); // 同步刷新监视器
            _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
        }

        public async Task ScanMusicFoldersAsync()
        {
            // 🌟 互斥并发锁：如果当前已在扫描，则直接拒绝本次触发
            if (!_scanLock.Wait(0)) return;

            try
            {
                var folders = GetMusicFolders();
                bool dbChanged = false;

                // 1. 清理物理上已经不存在、或不再属于当前音乐文件夹的记录
                int deletedCount = await Task.Run(() =>
                {
                    int missing = _databaseHelper.CleanupMissingFiles();
                    int outsideFolders = _databaseHelper.CleanupTracksOutsideMusicFolders(folders);
                    return missing + outsideFolders;
                });
                if (deletedCount > 0)
                {
                    dbChanged = true;
                }

                var existingTracks = _databaseHelper.GetAllTracks().ToDictionary(t => t.FilePath, t => t);
                
                var allFiles = folders.Where(System.IO.Directory.Exists)
                    .SelectMany(folder => System.IO.Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Distinct()
                    .ToList();

                // --- A/B 融合探测与过滤 ---
                var filesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var abPairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // A -> B

                foreach (var f in allFiles)
                {
                    string partB = _metadataService.FindPartB(f);
                    if (!string.IsNullOrEmpty(partB) && allFiles.Contains(partB, StringComparer.OrdinalIgnoreCase))
                    {
                        filesToIgnore.Add(partB);
                        abPairs[f] = partB;
                    }
                }

                var filteredFiles = allFiles.Where(f => !filesToIgnore.Contains(f)).ToList();

                var filesToScan = filteredFiles.Where(f => 
                    !existingTracks.TryGetValue(f, out var existing) || 
                    System.IO.File.GetLastWriteTime(f) > existing.LastModified ||
                    (abPairs.ContainsKey(f) && existing.LoopStart == 0) // 如果是 A/B 但之前没设置过循环点，也重新扫一次
                ).ToList();

                // 2. 扫描新增或更新的文件记录
                if (filesToScan.Count > 0)
                {
                    var newOrUpdatedTracks = await Task.Run(() => 
                        filesToScan.AsParallel()
                        .WithDegreeOfParallelism(Environment.ProcessorCount)
                        .Select(f => CreateTrackFromFile(f, abPairs.ContainsKey(f) ? abPairs[f] : null))
                        .Where(t => t != null)
                        .ToList()
                    );

                    if (newOrUpdatedTracks.Count > 0)
                    {
                        _databaseHelper.BulkInsert(newOrUpdatedTracks);
                        
                        // 扫描完成后，触发一次全局封面修复逻辑
                        _databaseHelper.RepairMissingCategoryCovers();
                        dbChanged = true;
                    }
                }

                // 🌟 对已有曲目中 DurationMs <= 0 的文件进行回填扫描（非阻塞，在当前扫描批次内完成）
                var zeroDurationTracks = existingTracks.Values
                    .Where(t => t.DurationMs <= 0 && System.IO.File.Exists(t.FilePath))
                    .ToList();
                if (zeroDurationTracks.Count > 0)
                {
                    await Task.Run(() =>
                    {
                        foreach (var t in zeroDurationTracks)
                        {
                            try
                            {
                                using var tf = TagLib.File.Create(t.FilePath);
                                long ms = (long)tf.Properties.Duration.TotalMilliseconds;
                                if (ms > 0)
                                {
                                    t.DurationMs = ms;
                                    _databaseHelper.SaveTrack(t);
                                }
                            }
                            catch { /* 跳过无法读取的文件 */ }
                        }
                    });
                    dbChanged = true;
                }

                // 🌟 如果数据库有了新增、更新或删除变动，发布全局库刷新事件，通知 UI 重新加载
                if (dbChanged)
                {
                    _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                }
            }
            finally
            {
                _scanLock.Release(); // 释放锁
            }
        }

        private MusicTrack CreateTrackFromFile(string filePath, string partBPath = null)
        {
            try
            {
                using var audioFile = TagLib.File.Create(filePath);
                long durationMsA = (long)audioFile.Properties.Duration.TotalMilliseconds;
                long samplesA = (long)(audioFile.Properties.AudioSampleRate * audioFile.Properties.Duration.TotalSeconds);
                long totalSamples = samplesA;
                long durationMs = durationMsA;
                long loopStart = 0;

                if (!string.IsNullOrEmpty(partBPath) && System.IO.File.Exists(partBPath))
                {
                    // 核心修复：对 A/B 歌曲，必须使用 NAudio 计算采样数，
                    // 因为 TagLib 的 Duration（来自文件头）与 NAudio 实际解码长度会有不固定的差异，
                    // 导致数据库存储的 LoopStart/LoopEnd 与加载时 AudioLooper 计算的衔接点不匹配。
                    try
                    {
                        using (var readerA = CreateNAudioStream(filePath))
                        using (var readerB = CreateNAudioStream(partBPath))
                        {
                            if (readerA != null && readerB != null)
                            {
                                samplesA = readerA.Length / readerA.WaveFormat.BlockAlign;
                                long samplesB = readerB.Length / readerB.WaveFormat.BlockAlign;
                                totalSamples = samplesA + samplesB;

                                // A/B 拼接时长 = A 文件 + B 文件各自的毫秒数
                                double totalSecondsA = (double)readerA.Length / readerA.WaveFormat.AverageBytesPerSecond;
                                double totalSecondsB = (double)readerB.Length / readerB.WaveFormat.AverageBytesPerSecond;
                                durationMs = (long)((totalSecondsA + totalSecondsB) * 1000);
                            }
                            else
                            {
                                // NAudio 加载失败时回退到 TagLib 的估算值
                                using var partBFile = TagLib.File.Create(partBPath);
                                long samplesB = (long)(partBFile.Properties.AudioSampleRate * partBFile.Properties.Duration.TotalSeconds);
                                totalSamples += samplesB;
                                durationMs = durationMsA + (long)partBFile.Properties.Duration.TotalMilliseconds;
                            }
                        }
                    }
                    catch
                    {
                        // NAudio 异常时回退到 TagLib
                        using var partBFile = TagLib.File.Create(partBPath);
                        long samplesB = (long)(partBFile.Properties.AudioSampleRate * partBFile.Properties.Duration.TotalSeconds);
                        totalSamples = samplesA + samplesB;
                        durationMs = durationMsA + (long)partBFile.Properties.Duration.TotalMilliseconds;
                    }
                    loopStart = samplesA; // 默认循环起点设在衔接处
                }

                var track = new MusicTrack
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    DisplayName = !string.IsNullOrEmpty(audioFile.Tag.Title) ? audioFile.Tag.Title : Path.GetFileNameWithoutExtension(filePath),
                    Artist = audioFile.Tag.FirstPerformer,
                    Album = audioFile.Tag.Album,
                    AlbumArtist = audioFile.Tag.FirstAlbumArtist,
                    TotalSamples = totalSamples,
                    DurationMs = durationMs,
                    LoopStart = loopStart,
                    LoopEnd = totalSamples,
                    LastModified = System.IO.File.GetLastWriteTime(filePath)
                };

                // 获取封面
                track.CoverPath = _metadataService.GetOrExtractCover(track);

                return track;
            }
            catch
            {
                return null;
            }
        }

        public async Task<(int tracks, int playlists)> SyncDatabaseAsync(string externalDbPath)
        {
            if (string.IsNullOrEmpty(externalDbPath) || !System.IO.File.Exists(externalDbPath))
                return (0, 0);

            var result = await Task.Run(() => _databaseHelper.SyncWithExternalDatabase(externalDbPath));
            
            // 同步完成后也跑一遍修复逻辑
            _databaseHelper.RepairMissingCategoryCovers();
            
            return result;
        }

        /// <summary>
        /// 使用 NAudio 创建音频流（与 AudioLooper.CreateAudioStream 保持一致）
        /// 确保扫描时和加载时的采样数计算方式完全相同
        /// </summary>
        private WaveStream CreateNAudioStream(string filePath)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLower();
                switch (ext)
                {
                    case ".wav":
                        return new WaveFileReader(filePath);
                    case ".ogg":
                        return new NAudio.Vorbis.VorbisWaveReader(filePath);
                    case ".mp3":
                        return new Mp3FileReader(filePath);
                    case ".flac":
                        // 与播放端保持一致：FLAC 使用 bundled reader，确保采样数计算和实际加载一致。
                        return new FlacReader(filePath);
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose() { }
    }
}
