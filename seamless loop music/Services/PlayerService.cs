using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using NAudio.Vorbis;
using System.Threading.Tasks;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 播放模式
    /// </summary>
    public enum PlayMode
    {
        SingleLoop,   // 单曲无缝循环 (默认)
        ListLoop,     // 列表循环 (设定循环次数后切换)
        Shuffle       // 随机播放
    }

    /// <summary>
    /// 核心播放业务逻辑服务
    /// 把 MainWindow 从繁重的 Audio+DB 协调工作中解放出来
    /// </summary>
    public class PlayerService : IDisposable
    {
        private readonly AudioLooper _audioLooper;
        private readonly DatabaseHelper _dbHelper;
        private readonly LoopAnalysisService _loopAnalysisService;
        private readonly PlaylistManagerService _playlistManager;
        private readonly PlaybackQueueService _playbackQueue;
        private readonly TrackMetadataService _trackMetadata;
        private readonly Random _random = new Random();
        
        // 播放状态相关
        public List<MusicTrack> Playlist { get => _playbackQueue.Playlist; set => _playbackQueue.Playlist = value; }
        public int CurrentIndex { get => _playbackQueue.CurrentIndex; set => _playbackQueue.CurrentIndex = value; }
        public PlayMode CurrentMode { get => _playbackQueue.CurrentMode; set => _playbackQueue.CurrentMode = value; }
        public int LoopLimit { get => _playbackQueue.LoopLimit; set => _playbackQueue.LoopLimit = value; }
        private int _currentLoopCount = 0;
        private bool _isConcatenatedLoad = false; // 标记当前加载是否为 A+B 物理合体模式

        public MusicTrack CurrentTrack { get; private set; }
        public bool IsABMode => _isConcatenatedLoad; // 是否处于 A+B 物理合体模式

        public event Action<MusicTrack> OnTrackLoaded;
        public event Action<PlaybackState> OnPlayStateChanged;
        public event Action<string> OnStatusMessage; // 统一的消息通知
        public event Action<int> OnIndexChanged;   // 通知 UI 更新选中项

        public PlayerService()
        {
            _audioLooper = new AudioLooper();
            _dbHelper = new DatabaseHelper();
            _loopAnalysisService = new LoopAnalysisService();
            _loopAnalysisService.OnStatusMessage += msg => {
                string localized = msg;
                if (msg.StartsWith("LOC:")) {
                    string key = msg.Substring(4);
                    localized = LocalizationService.Instance[key];
                }
                OnStatusMessage?.Invoke(localized);
            };
            _playlistManager = new PlaylistManagerService(_dbHelper);
            _playlistManager.OnStatusMessage = msg => OnStatusMessage?.Invoke(msg);
            _playbackQueue = new PlaybackQueueService();
            _playbackQueue.OnIndexChanged += idx => OnIndexChanged?.Invoke(idx);
            _trackMetadata = new TrackMetadataService(_dbHelper);

            // 转发底层事件
            _audioLooper.OnPlayStateChanged += state => OnPlayStateChanged?.Invoke(state);
            _audioLooper.OnStatusChanged += msg => OnStatusMessage?.Invoke(msg);
            
            // 核心加载回调：当音频加载完成，立即进行数据库匹配和数据组装
            _audioLooper.OnAudioLoaded += HandleAudioLoaded;
            
            // 循环完成回调
            _audioLooper.OnLoopCycleCompleted += HandleLoopCycleCompleted;
        }

        public void SetPyMusicLooperCachePath(string path)
        {
            _loopAnalysisService.SetCustomCachePath(path);
        }

        public void SetPyMusicLooperExecutablePath(string path)
        {
            _loopAnalysisService.SetPyMusicLooperExecutablePath(path);
        }

        public async Task<int> CheckPyMusicLooperStatusAsync()
        {
            return await _loopAnalysisService.CheckEnvironmentAsync();
        }

        private void HandleLoopCycleCompleted()
        {
            _currentLoopCount++;
            
            // 如果不是单曲循环模式，且达到了循环次数限制，就切歌
            if (CurrentMode != PlayMode.SingleLoop && _currentLoopCount >= LoopLimit)
            {
                // 关键修复：使用 Task.Run 异步切换，避免在音频读取线程中直接调用 Stop/Dispose 导致死锁
                System.Threading.Tasks.Task.Run(() => {
                    OnStatusMessage?.Invoke($"Loop limit reached ({LoopLimit}), switching to next...");
                    NextTrack();
                });
            }
        }

        // --- 核心业务逻辑 ---

        /// <summary>
        /// 切换到下一首
        /// </summary>
        public void NextTrack()
        {
            string path = _playbackQueue.GetNextTrackPath();
            if (path != null) LoadTrack(path, true);
        }

        /// <summary>
        /// 切换到上一首
        /// </summary>
        public void PreviousTrack()
        {
            string path = _playbackQueue.GetPreviousTrackPath();
            if (path != null) LoadTrack(path, true);
        }

        /// <summary>
        /// 播放指定索引的曲目
        /// </summary>
        public void PlayAtIndex(int index)
        {
            string path = _playbackQueue.GetTrackPathAtIndex(index);
            if (path != null) 
            {
                _currentLoopCount = 0;
                LoadTrack(path, true);
            }
        }

        /// <summary>
        /// 加载并初始化一首曲目 (整合了文件加载 + 数据库查询)
        /// 现在会自动同步歌单索引
        /// </summary>
        public void LoadTrack(string filePath, bool autoPlay = false)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // 1. 文件丢失检查
            if (!File.Exists(filePath))
            {
                OnStatusMessage?.Invoke($"❌ 文件丢失: {Path.GetFileName(filePath)}");
                
                // 如果是自动播放模式，尝试跳过
                if (CurrentMode != PlayMode.SingleLoop)
                {
                    System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => NextTrack());
                }
                return;
            }

            // 同步队列索引
            int idx = _playbackQueue.FindIndexByPath(filePath);
            if (idx != -1) _playbackQueue.CurrentIndex = idx;

            // 获取现有曲目数据
            CurrentTrack = (idx != -1) ? _playbackQueue.Playlist[idx] : new MusicTrack { FilePath = filePath, FileName = Path.GetFileName(filePath) };
            
            _currentLoopCount = 0; 
            
            // --- A/B 循环逻辑探测 ---
            string partB = _trackMetadata.FindPartB(filePath);
            _isConcatenatedLoad = !string.IsNullOrEmpty(partB);

            if (_isConcatenatedLoad) _audioLooper.LoadAudio(filePath, partB);
            else _audioLooper.LoadAudio(filePath);

            if (autoPlay) Play();
        }


        /// <summary>
        /// 处理音频加载完成后的数据整合
        /// </summary>
        private void HandleAudioLoaded(long totalSamples, int sampleRate)
        {
            if (CurrentTrack == null) return;

            // 1. 获取/更新元数据
            long defaultLoopStart = _audioLooper.LoopStartSample;
            CurrentTrack = _trackMetadata.GetOrUpdateTrackMetadata(CurrentTrack.FilePath, totalSamples, defaultLoopStart, _isConcatenatedLoad);

            // 2. 同步回 Playlist 中的对象
            if (Playlist != null && CurrentIndex >= 0 && CurrentIndex < Playlist.Count)
            {
                Playlist[CurrentIndex] = CurrentTrack;
            }

            // 3. 将配置应用到播放器
            _audioLooper.SetLoopStartSample(CurrentTrack.LoopStart);
            _audioLooper.SetLoopEndSample(CurrentTrack.LoopEnd);

            // 4. 通知 UI
            OnTrackLoaded?.Invoke(CurrentTrack);
        }

        /// <summary>
        /// 保存当前曲目配置 (无论是改名还是改循环点，统一用这个)
        /// </summary>
        public void SaveCurrentTrack()
        {
            if (CurrentTrack == null) return;

            CurrentTrack.LoopStart = _audioLooper.LoopStartSample;
            CurrentTrack.LoopEnd = _audioLooper.LoopEndSample;

            try {
                _trackMetadata.SaveTrack(CurrentTrack);
            } catch (Exception ex) {
                OnStatusMessage?.Invoke($"Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 重命名并保存
        /// </summary>
        public void RenameCurrentTrack(string newName)
        {
            if (CurrentTrack == null) return;
            
            CurrentTrack.DisplayName = newName;
            SaveCurrentTrack(); // 立即保存，确保持久化
        }

        /// <summary>
        /// 智能匹配循环点 (逆向：固定 End，找 Start)
        /// </summary>
        public void SmartMatchLoopReverseAsync(Action onComplete = null) => SmartMatchLoopInternal(true, onComplete);

        /// <summary>
        /// 智能匹配循环点 (正向：固定 Start，找 End)
        /// </summary>
        public void SmartMatchLoopForwardAsync(Action onComplete = null) => SmartMatchLoopInternal(false, onComplete);

        /// <summary>
        /// 恢复 A/B 模式的原始接缝位置 (LoopStart 即 A 段末尾)
        /// </summary>
        public void ResetABLoopPoints()
        {
            if (CurrentTrack == null) return;

            string partB = _trackMetadata.FindPartB(CurrentTrack.FilePath);
            if (string.IsNullOrEmpty(partB))
            {
                OnStatusMessage?.Invoke("Current track is not an A/B pair.");
                return;
            }

            long samplesA = _playlistManager.GetTotalSamples(CurrentTrack.FilePath);
            if (samplesA <= 0) return;

            long samplesB = _playlistManager.GetTotalSamples(partB);
            long total = samplesA + samplesB;

            CurrentTrack.LoopStart = samplesA;
            CurrentTrack.LoopEnd = total;
            CurrentTrack.TotalSamples = total;

            _audioLooper.SetLoopStartSample(CurrentTrack.LoopStart);
            _audioLooper.SetLoopEndSample(CurrentTrack.LoopEnd);

            SaveCurrentTrack();
            OnTrackLoaded?.Invoke(CurrentTrack);
            OnStatusMessage?.Invoke("Restored to original A/B boundary.");
        }

        public void UpdatePlaylistsSortOrder(List<int> playlistIds) => _playlistManager.UpdatePlaylistsSortOrder(playlistIds);

        public void UpdateTracksSortOrder(int playlistId, List<int> songIds) => _playlistManager.UpdateTracksSortOrder(playlistId, songIds);

        private void SmartMatchLoopInternal(bool adjustStart, Action onComplete)
        {
            if (CurrentTrack == null) return;
            
            long start = _audioLooper.LoopStartSample;
            long end = _audioLooper.LoopEndSample;
            
            string modeStr = adjustStart ? "Reverse (Fix End)" : "Forward (Fix Start)";
            OnStatusMessage?.Invoke($"Smart Matching {modeStr} Async...");

            _audioLooper.FindBestLoopPointsAsync(start, end, adjustStart, (newStart, newEnd) => 
            {
                // 1. 应用新配置
                _audioLooper.SetLoopStartSample(newStart);
                _audioLooper.SetLoopEndSample(newEnd);
                
                // 2. 更新当前 Track 对象
                CurrentTrack.LoopStart = newStart;
                CurrentTrack.LoopEnd = newEnd;
                
                // 3. 立即保存
                SaveCurrentTrack();
                
                OnStatusMessage?.Invoke($"Smart Match Applied: Start={newStart}, End={newEnd}");
                
                // 4. 通知调用者 (UI) 刷新
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 使用 PyMusicLooper 进行外部终极匹配 (方案 A: 通过 uv 运行源码)
        /// </summary>
        public async void SmartMatchLoopExternalAsync(Action onComplete = null)
        {
            if (CurrentTrack == null) return;
            
            OnStatusMessage?.Invoke($"[PyMusicLooper] Analyzing {CurrentTrack.FileName} via uv...");

            await System.Threading.Tasks.Task.Run(async () =>
            {
                var result = await _loopAnalysisService.FindBestLoopAsync(CurrentTrack.FilePath);
                
                if (result.HasValue)
                {
                    var (start, end, score) = result.Value;
                    
                    // 应用并保存
                    _audioLooper.SetLoopStartSample(start);
                    _audioLooper.SetLoopEndSample(end);
                    
                    CurrentTrack.LoopStart = start;
                    CurrentTrack.LoopEnd = end;
                    SaveCurrentTrack();
                    
                    OnStatusMessage?.Invoke($"[PyMusicLooper] Found! Score: {score:P2}. Range: {start}-{end}");
                }
                else
                {
                    OnStatusMessage?.Invoke("[PyMusicLooper] Analysis failed. Please check uv/source path.");
                }
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 获取当前歌曲的候选循环点列表（Top 10）
        /// 优先读取数据库缓存
        /// </summary>
        public async Task<List<LoopCandidate>> GetLoopCandidatesAsync(bool forceRefresh = false)
        {
            if (CurrentTrack == null) return new List<LoopCandidate>();

            // 1. 尝试从数据库缓存读取
            if (!forceRefresh && !string.IsNullOrEmpty(CurrentTrack.LoopCandidatesJson))
            {
                try 
                {
                     // 简单的 JSON 反序列化 (使用 System.Text.Json 或 Newtonsoft)
                     // 由于环境不确定，这里假设可以用 Newtonsoft (这是 WPF 常备) 
                     // 或者手写个简易 Parser 如果没有库 (LoopCandidate 很简单)
                     // 这里我使用 Newtonsoft.Json (JsonConvert.DeserializeObject)，如果不通过再换。
                     // 实际上，为了保险，我先检测引用。
                     // 既然不能检测，我用 System.Text.Json (Core) 或 JavaScriptSerializer (Framework)
                     // 假定是 .NET Framework 且没有 nuget，不仅麻烦。
                     
                     // 使用内置简单解析器
                     var cached = _loopAnalysisService.DeserializeLoopCandidates(CurrentTrack.LoopCandidatesJson);
                     if (cached != null && cached.Count > 0)
                     {
                         OnStatusMessage?.Invoke($"[Cache] Loaded {cached.Count} loop candidates from database.");
                         return cached;
                     }
                }
                catch (Exception ex) 
                {
                    System.Diagnostics.Debug.WriteLine("JSON Parse Error: " + ex.Message);
                }
            }
            
            OnStatusMessage?.Invoke($"[PyMusicLooper] Fetching top candidates for {CurrentTrack.FileName}...");
            var candidates = await _loopAnalysisService.FetchTopLoopCandidatesAsync(CurrentTrack.FilePath);

            // 2. 存入缓存
            if (candidates != null && candidates.Count > 0)
            {
                try
                {
                    string json = _loopAnalysisService.SerializeLoopCandidates(candidates);
                    CurrentTrack.LoopCandidatesJson = json;
                    SaveCurrentTrack(); // 持久化到数据库
                }
                 catch (Exception ex) 
                 {
                     System.Diagnostics.Debug.WriteLine("JSON Save Error: " + ex.Message);
                 }
            }

            if (candidates != null && candidates.Count > 0)
                 OnStatusMessage?.Invoke("[PyMusicLooper] Candidates updated.");
            else
                 OnStatusMessage?.Invoke("[PyMusicLooper] No candidates found.");

            return candidates;
        }



        /// <summary>
        /// 应用指定的循环点候选
        /// </summary>
        public void ApplyLoopCandidate(LoopCandidate candidate)
        {
            if (candidate == null) return;
            // 自动播放以进行测试：跳转到循环结束前 3 秒处，方便用户确认衔接效果
            ApplyLoop(candidate.LoopStart, candidate.LoopEnd, true);
        }

        private void ApplyLoop(long start, long end, bool autoPlay = false)
        {
             if (CurrentTrack != null)
             {
                CurrentTrack.LoopStart = start;
                CurrentTrack.LoopEnd = end;
                
                _audioLooper.SetLoopStartSample(start);
                _audioLooper.SetLoopEndSample(end);
                
                SaveCurrentTrack();
                OnTrackLoaded?.Invoke(CurrentTrack);
                
                OnStatusMessage?.Invoke($"Loop Applied: {start} - {end}");

                if (autoPlay)
                {
                    // 跳转到循环结束前 3 秒 (或者循环长度的一半，如果循环很短)
                    long leadIn = 3L * SampleRate;
                    long loopLen = end - start;
                    if (leadIn > loopLen) leadIn = loopLen / 2; // 如果循环特别短，至少听一半
                    
                    long seekTarget = end - leadIn;
                    
                    // 确保不要跳到负数（虽然理论上不会，因为 start >= 0）
                    if (seekTarget < 0) seekTarget = 0; 
                    
                    SeekToSample(seekTarget);
                    Play();
                }
             }
        }



        /// <summary>
        /// 批量对指定的歌曲列表进行 PyMusicLooper 极致优化
        /// </summary>
        public async Task BatchSmartMatchLoopExternalAsync(List<MusicTrack> tracks, Action<int, int, string> onProgress, Action onComplete)
        {
            if (tracks == null || tracks.Count == 0) {
                onComplete?.Invoke();
                return;
            }

            await System.Threading.Tasks.Task.Run(async () =>
            {
                int total = tracks.Count;
                int current = 0;

                foreach (var track in tracks)
                {
                    current++;
                    onProgress?.Invoke(current, total, track.FileName);

                    try 
                    {
                        // 1. 获取排行榜 (所有候选点)
                        var candidates = await _loopAnalysisService.FetchTopLoopCandidatesAsync(track.FilePath);
                        
                        if (candidates != null && candidates.Count > 0)
                        {
                            // 2. 序列化并保存排行榜
                            try {
                                track.LoopCandidatesJson = _loopAnalysisService.SerializeLoopCandidates(candidates);
                            } catch { }

                            // 3. 选取分数最高的应用
                            var best = candidates.OrderByDescending(c => c.Score).FirstOrDefault();
                            if (best != null)
                            {
                                track.LoopStart = best.LoopStart;
                                track.LoopEnd = best.LoopEnd;
                                
                                _dbHelper.SaveTrack(track); 

                                // 实时更新当前播放状态
                                if (CurrentTrack != null && CurrentTrack.FilePath == track.FilePath)
                                {
                                    CurrentTrack.LoopCandidatesJson = track.LoopCandidatesJson;
                                    CurrentTrack.LoopStart = track.LoopStart;
                                    CurrentTrack.LoopEnd = track.LoopEnd;
                                    
                                    _audioLooper.SetLoopStartSample(track.LoopStart);
                                    _audioLooper.SetLoopEndSample(track.LoopEnd);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         OnStatusMessage?.Invoke($"[Batch] Error on {track.FileName}: {ex.Message}");
                    }
                }
                onComplete?.Invoke();
            });
        }

        // --- 播放控制透传 ---

        public void Play() => _audioLooper.Play();
        public void Pause() => _audioLooper.Pause();
        public void Stop() => _audioLooper.Stop();
        public void Seek(double percent) => _audioLooper.Seek(percent);
        public void SeekToSample(long sample) => _audioLooper.SeekToSample(sample);
        public void SetVolume(float volume) => _audioLooper.Volume = volume;

        // --- 属性透传 ---
        
        public PlaybackState PlaybackState => _audioLooper.PlaybackState;
        public TimeSpan CurrentTime => _audioLooper.CurrentTime;
        public TimeSpan TotalTime => _audioLooper.TotalTime;
        public long LoopStartSample => _audioLooper.LoopStartSample;
        public long LoopEndSample => _audioLooper.LoopEndSample;
        public int SampleRate => _audioLooper.SampleRate; 

        public void SetLoopStart(long sample) => _audioLooper.SetLoopStartSample(sample);
        public void SetLoopEnd(long sample) => _audioLooper.SetLoopEndSample(sample);

        public void ImportTracks(IEnumerable<MusicTrack> tracks)
        {
            _dbHelper.BulkInsert(tracks);
        }

        // --- 新增：歌单操作透传 ---
        
        /// <summary>
        /// 获取所有歌单（数据库驱动）
        /// </summary>
        // ---  Playlist Operations Delegation ---

        public List<PlaylistFolder> GetAllPlaylists() => _playlistManager.GetAllPlaylists();
        public int CreatePlaylist(string name, string folderPath = null, bool isLinked = false) => _playlistManager.CreatePlaylist(name, folderPath, isLinked);
        public void RenamePlaylist(int playlistId, string newName) => _playlistManager.RenamePlaylist(playlistId, newName);
        public void DeletePlaylist(int playlistId) => _playlistManager.DeletePlaylist(playlistId);
        
        public void AddTrackToPlaylist(int playlistId, MusicTrack track) => _playlistManager.AddTrackToPlaylist(playlistId, track);
        public void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks) => _playlistManager.AddTracksToPlaylist(playlistId, tracks);
        public void RemoveTrackFromPlaylist(int playlistId, int songId) => _playlistManager.RemoveTrackFromPlaylist(playlistId, songId);
        
        public Task AddFilesToPlaylist(int playlistId, string[] filePaths) => _playlistManager.AddFilesToPlaylistAsync(playlistId, filePaths);
        public Task AddFolderToPlaylist(int playlistId, string folderPath) => _playlistManager.AddFolderToPlaylistAsync(playlistId, folderPath);
        public Task RemoveFolderFromPlaylist(int playlistId, string folderPath) => _playlistManager.RemoveFolderFromPlaylistAsync(playlistId, folderPath);
        public List<string> GetPlaylistFolders(int playlistId) => _playlistManager.GetPlaylistFolders(playlistId);
        public Task RefreshPlaylist(int playlistId) => _playlistManager.RefreshPlaylistAsync(playlistId);
        
        public List<MusicTrack> LoadPlaylistFromDb(int playlistId) => _playlistManager.LoadPlaylistFromDb(playlistId);
        
        public MusicTrack GetStoredTrackInfo(string filePath) => _playlistManager.GetStoredTrackInfo(filePath);
        public void UpdateOfflineTrack(MusicTrack track) => _playlistManager.UpdateOfflineTrack(track);

        public long GetTotalSamples(string filePath) => _playlistManager.GetTotalSamples(filePath);



        public void Dispose()
        {
            _audioLooper?.Dispose();
        }
    }
}
