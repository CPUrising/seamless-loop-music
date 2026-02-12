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
        private readonly PyMusicLooperWrapper _pyMusicLooperWrapper; // 引用新的 Wrapper
        private readonly Random _random = new Random();
        
        // 播放相关
        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        public int CurrentIndex { get; set; } = -1;
        public PlayMode CurrentMode { get; set; } = PlayMode.SingleLoop;
        public int LoopLimit { get; set; } = 1; // 列表模式下，每首歌循环几次后切换？
        private int _currentLoopCount = 0;
        private bool _isConcatenatedLoad = false; // 标记当前加载是否为 A+B 物理合体模式

        // 当前正在操作的音乐对象（包含 ID、路径、元数据）
        public MusicTrack CurrentTrack { get; private set; }

        public event Action<MusicTrack> OnTrackLoaded;
        public event Action<PlaybackState> OnPlayStateChanged;
        public event Action<string> OnStatusMessage; // 统一的消息通知
        public event Action<int> OnIndexChanged;   // 通知 UI 更新选中项

        public PlayerService()
        {
            _audioLooper = new AudioLooper();
            _dbHelper = new DatabaseHelper();
            _pyMusicLooperWrapper = new PyMusicLooperWrapper(); // 初始化

            // 转发底层事件
            _audioLooper.OnPlayStateChanged += state => OnPlayStateChanged?.Invoke(state);
            _audioLooper.OnStatusChanged += msg => OnStatusMessage?.Invoke(msg);
            
            // 核心加载回调：当音频加载完成，立即进行数据库匹配和数据组装
            _audioLooper.OnAudioLoaded += HandleAudioLoaded;
            
            // 循环完成回调
            _audioLooper.OnLoopCycleCompleted += HandleLoopCycleCompleted;
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
            if (Playlist == null || Playlist.Count == 0) return;

            int nextIndex;
            if (CurrentMode == PlayMode.Shuffle)
            {
                // 使用类级别的随机逻辑
                nextIndex = _random.Next(0, Playlist.Count);
            }
            else
            {
                nextIndex = (CurrentIndex + 1) % Playlist.Count;
            }

            PlayAtIndex(nextIndex);
        }

        /// <summary>
        /// 切换到上一首
        /// </summary>
        public void PreviousTrack()
        {
            if (Playlist == null || Playlist.Count == 0) return;

            int prevIndex = (CurrentIndex - 1 + Playlist.Count) % Playlist.Count;
            PlayAtIndex(prevIndex);
        }

        /// <summary>
        /// 播放指定索引的曲目
        /// </summary>
        public void PlayAtIndex(int index)
        {
            if (Playlist == null || index < 0 || index >= Playlist.Count) return;

            _currentLoopCount = 0; // 重置循环计数
            CurrentIndex = index;
            OnIndexChanged?.Invoke(index);
            LoadTrack(Playlist[index].FilePath);
            Play();
        }

        /// <summary>
        /// 加载并初始化一首曲目 (整合了文件加载 + 数据库查询)
        /// 现在会自动同步歌单索引
        /// </summary>
        public void LoadTrack(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // 1. 文件丢失检查
            if (!File.Exists(filePath))
            {
                OnStatusMessage?.Invoke($"❌ 文件丢失: {Path.GetFileName(filePath)}");
                
                // 如果是自动播放模式，尝试跳过（延迟一下避免狂刷）
                if (CurrentMode != PlayMode.SingleLoop)
                {
                    System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => NextTrack());
                }
                return;
            }
            
            // 1. 获取现有对象或创建占位符
            MusicTrack existingInPlaylist = null;
            if (Playlist != null)
            {
                int newIndex = Playlist.FindIndex(t => t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                if (newIndex != -1)
                {
                    CurrentIndex = newIndex;
                    existingInPlaylist = Playlist[newIndex];
                    OnIndexChanged?.Invoke(newIndex);
                }
            }

            // 使用现有对象或创建新的
            CurrentTrack = existingInPlaylist ?? new MusicTrack { FilePath = filePath, FileName = Path.GetFileName(filePath) };
            
            _currentLoopCount = 0; // 只要换了歌，循环计数就归零

            // --- A/B 循环逻辑探测 ---
            string partB = FindPartB(filePath);
            _isConcatenatedLoad = !string.IsNullOrEmpty(partB);

            if (_isConcatenatedLoad)
            {
                OnStatusMessage?.Invoke($"[A/B Mode] Fusing Intro and Loop segments...");
                _audioLooper.LoadAudio(filePath, partB);
            }
            else
            {
                _audioLooper.LoadAudio(filePath); 
            }
        }

        private string FindPartB(string filePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);

                string[] aSuffixes = { "_A", "_a", "_intro", "_Intro" };
                string[] bSuffixes = { "_B", "_b", "_loop", "_Loop" };

                for (int i = 0; i < aSuffixes.Length; i++)
                {
                    if (fileName.EndsWith(aSuffixes[i]))
                    {
                        string baseName = fileName.Substring(0, fileName.Length - aSuffixes[i].Length);
                        string bName = baseName + bSuffixes[i];
                        string bPath = Path.Combine(dir, bName + ext);
                        if (File.Exists(bPath)) return bPath;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 处理音频加载完成后的数据整合
        /// </summary>
        private void HandleAudioLoaded(long totalSamples, int sampleRate)
        {
            if (CurrentTrack == null) return;

            CurrentTrack.TotalSamples = totalSamples;
            
            // 2. 去数据库查户口 (指纹匹配)
            var dbTrack = _dbHelper.GetTrack(CurrentTrack.FilePath, totalSamples);
            
            // 模糊匹配：如果采样数变了，尝试通过文件名找回之前的 ID
            if (dbTrack == null)
            {
                dbTrack = _dbHelper.GetAllTracks().FirstOrDefault(t => 
                    Path.GetFileName(t.FilePath).Equals(Path.GetFileName(CurrentTrack.FilePath), StringComparison.OrdinalIgnoreCase));
            }

            if (dbTrack != null)
            {
                CurrentTrack.Id = dbTrack.Id;
                CurrentTrack.DisplayName = dbTrack.DisplayName;
                
                // --- A/B 自动修正 ---
                // 如果当前是 A+B 模式，但存档还停留在“单曲模式”（LoopStart 为 0）
                // 或者是采样数发生了巨大变化（说明刚合并了 B）
                if (_isConcatenatedLoad && (dbTrack.LoopStart == 0 || Math.Abs(dbTrack.TotalSamples - totalSamples) > 1000))
                {
                    CurrentTrack.LoopStart = _audioLooper.LoopStartSample; 
                    CurrentTrack.LoopEnd = totalSamples;
                    CurrentTrack.TotalSamples = totalSamples;
                    SaveCurrentTrack(); // 升级存档
                }
                else
                {
                    CurrentTrack.LoopStart = dbTrack.LoopStart;
                    CurrentTrack.LoopEnd = (dbTrack.LoopEnd <= 0) ? totalSamples : dbTrack.LoopEnd;
                }
            }
            else
            {
                // 纯新人
                CurrentTrack.LoopStart = _isConcatenatedLoad ? _audioLooper.LoopStartSample : 0;
                CurrentTrack.LoopEnd = totalSamples;
                SaveCurrentTrack(); // 新歌落户
            }

            // 同步回 Playlist 中的对象（如果存在的话）
            if (Playlist != null && CurrentIndex >= 0 && CurrentIndex < Playlist.Count)
            {
                var pTrack = Playlist[CurrentIndex];
                if (pTrack.FilePath == CurrentTrack.FilePath)
                {
                    pTrack.Id = CurrentTrack.Id;
                    pTrack.LoopStart = CurrentTrack.LoopStart;
                    pTrack.LoopEnd = CurrentTrack.LoopEnd;
                    pTrack.TotalSamples = CurrentTrack.TotalSamples;
                    pTrack.DisplayName = CurrentTrack.DisplayName;
                }
            }
            // 3. 将配置应用到播放器
            _audioLooper.SetLoopStartSample(CurrentTrack.LoopStart);
            _audioLooper.SetLoopEndSample(CurrentTrack.LoopEnd);

            // 4. 如果是新发现的歌（或者是 A+B 模式下时长变化的歌），自动存入数据库
            if (dbTrack == null)
            {
                SaveCurrentTrack();
            }

            // 5. 通知 UI
            OnTrackLoaded?.Invoke(CurrentTrack);
        }

        /// <summary>
        /// 保存当前曲目配置 (无论是改名还是改循环点，统一用这个)
        /// </summary>
        public void SaveCurrentTrack()
        {
            if (CurrentTrack == null) return;

            // 1. 从播放器获取最新的循环点
            CurrentTrack.LoopStart = _audioLooper.LoopStartSample;
            CurrentTrack.LoopEnd = _audioLooper.LoopEndSample;

            // 2. 存入数据库 (ID 稳定逻辑已在 Helper 中实现)
            try {
                _dbHelper.SaveTrack(CurrentTrack);
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
                var result = await _pyMusicLooperWrapper.FindBestLoopAsync(CurrentTrack.FilePath);
                
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
                        var result = await _pyMusicLooperWrapper.FindBestLoopAsync(track.FilePath);
                        if (result.HasValue)
                        {
                            track.LoopStart = result.Value.Start;
                            track.LoopEnd = result.Value.End;
                            _dbHelper.SaveTrack(track); // 批量模式下直接入库

                            // 如果当前正好在播放这首歌，顺便更新播放器状态
                            if (CurrentTrack != null && CurrentTrack.FilePath == track.FilePath)
                            {
                                _audioLooper.SetLoopStartSample(track.LoopStart);
                                _audioLooper.SetLoopEndSample(track.LoopEnd);
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
        public List<PlaylistFolder> GetAllPlaylists()
        {
            return _dbHelper.GetAllPlaylists().ToList();
        }

        public int CreatePlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            return _dbHelper.AddPlaylist(name, folderPath, isLinked);
        }

        public void RenamePlaylist(int playlistId, string newName)
        {
            _dbHelper.RenamePlaylist(playlistId, newName);
        }

        public void DeletePlaylist(int playlistId)
        {
            _dbHelper.DeletePlaylist(playlistId);
        }

        public void AddTrackToPlaylist(int playlistId, MusicTrack track)
        {
            if (track.Id <= 0)
            {
                 // 如果这首歌还没进资源库，先存一下拿到 ID
                 _dbHelper.SaveTrack(track);
            }
            _dbHelper.AddSongToPlaylist(playlistId, track.Id);
        }

        public void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks)
        {
             // 批量入库新歌（如果没入库的话）
             var newTracks = tracks.Where(t => t.Id <= 0).ToList();
             if (newTracks.Any())
             {
                 _dbHelper.BulkSaveTracksAndAddToPlaylist(newTracks, playlistId);
             }

             // 只有已存在的歌曲才可以通过 AddSongToPlaylist 关联，
             // 但 BulkSaveTracksAndAddToPlaylist 已经顺便把它们加进去了。
             // 所以这里只需要处理那些 "Id > 0" (已经在库里) 的歌曲，把它们关联到新歌单。
             
             var existingTracks = tracks.Where(t => t.Id > 0).ToList();
             foreach (var t in existingTracks)
             {
                 _dbHelper.AddSongToPlaylist(playlistId, t.Id);
             }
        }

        public void RemoveTrackFromPlaylist(int playlistId, int songId)
        {
            if (songId > 0)
            {
            _dbHelper.RemoveSongFromPlaylist(playlistId, songId);
            }
        }
        
        /// <summary>
        /// 添加多个文件到手动歌单（手动模式）
        /// </summary>
        public System.Threading.Tasks.Task AddFilesToPlaylist(int playlistId, string[] filePaths)
        {
             return System.Threading.Tasks.Task.Run(() => {
                var tracksToAdd = new List<MusicTrack>();
                
                foreach (var f in filePaths)
                {
                    try 
                    {
                        if (!File.Exists(f)) continue;
                        
                        // 1. 获取基础采样数
                        long samples = GetTotalSamples(f);
                        if (samples <= 0) continue;

                        var track = new MusicTrack 
                        { 
                            FilePath = f, 
                            FileName = Path.GetFileName(f), 
                            TotalSamples = samples,
                            LoopEnd = samples,
                            LoopStart = 0,
                            DisplayName = Path.GetFileNameWithoutExtension(f),
                            LastModified = DateTime.Now
                        };
                        tracksToAdd.Add(track);
                    }
                    catch { /* 忽略损坏文件 */ }
                }

                if (tracksToAdd.Count > 0)
                {
                    _dbHelper.BulkSaveTracksAndAddToPlaylist(tracksToAdd, playlistId);
                }
             });
        }


        public async System.Threading.Tasks.Task AddFolderToPlaylist(int playlistId, string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            // 1. 记录文件夹
            _dbHelper.AddPlaylistFolder(playlistId, folderPath);
            
            // 2. 刷新歌单内容
            await RefreshPlaylist(playlistId);
        }

        /// <summary>
        /// 刷新歌单内容：根据记录的文件夹重新扫描，添加新歌，移除消失的歌曲
        /// </summary>
        public System.Threading.Tasks.Task RefreshPlaylist(int playlistId)
        {
            return System.Threading.Tasks.Task.Run(() => {
                // 1. 获取所有关联文件夹
                var folders = _dbHelper.GetPlaylistFolders(playlistId);
                if (folders == null || folders.Count == 0) return;

                OnStatusMessage?.Invoke($"Refreshing playlist {playlistId} from {folders.Count} folders...");

                // 2. 扫描所有文件夹下的文件
                var disksFilesMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); // Path -> Samples

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;

                    var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(s => s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                                    s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                    s.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase));
                    
                    foreach (var f in files)
                    {
                        // 简单去重：如果多个文件夹包含同一个文件（不太可能），以后面的为准
                        if (!disksFilesMap.ContainsKey(f))
                        {
                            // 这里不立即读取 Sample，只记录路径，需进一步确认是否是新歌
                            disksFilesMap[f] = 0; 
                        }
                    }
                }

                // 3. 获取当前歌单内的歌曲
                var currentTracks = _dbHelper.GetPlaylistTracks(playlistId).ToList();
                var currentTrackPaths = new HashSet<string>(currentTracks.Select(t => t.FilePath), StringComparer.OrdinalIgnoreCase);

                // 4. 找出候选新增（或需要更新的）文件
                // 现在的逻辑：只要是磁盘上的音频文件，我们都重新扫一遍，确保 A/B 状态同步
                var allDiskFiles = disksFilesMap.Keys.ToList();
                
                // --- 过滤 B 文件 ---
                var aPartFiles = new List<string>();
                var bFilesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var f in allDiskFiles)
                {
                    string dir = Path.GetDirectoryName(f);
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    string ext = Path.GetExtension(f);
                    string[] bSuffixes = { "_B", "_b", "_loop", "_Loop" };
                    string[] aSuffixes = { "_A", "_a", "_intro", "_Intro" };

                    for (int i = 0; i < bSuffixes.Length; i++)
                    {
                        if (fileName.EndsWith(bSuffixes[i]))
                        {
                            string baseName = fileName.Substring(0, fileName.Length - bSuffixes[i].Length);
                            string aName = baseName + aSuffixes[i];
                            string aPath = Path.Combine(dir, aName + ext);
                            // 如果对应的 A 文件存在，那么这个 B 就不作为独立曲目入库
                            if (disksFilesMap.ContainsKey(aPath))
                            {
                                bFilesToIgnore.Add(f);
                                break;
                            }
                        }
                    }
                }

                foreach (var f in allDiskFiles)
                {
                    if (!bFilesToIgnore.Contains(f)) aPartFiles.Add(f);
                }

                // 5. 准备移除列表
                var tracksToRemove = new List<int>();
                foreach (var track in currentTracks)
                {
                    if (!disksFilesMap.ContainsKey(track.FilePath))
                    {
                        tracksToRemove.Add(track.Id);
                    }
                    else 
                    {
                        // 逻辑隐藏：已经是 B 文件但其 A 存在的，也要移除（清理旧历史）
                        string f = track.FilePath;
                        if (bFilesToIgnore.Contains(f))
                        {
                            tracksToRemove.Add(track.Id);
                            goto nextTrack;
                        }
                    }
                    nextTrack:;
                }

                // --- 执行数据库更新 ---

                // A. 核心：分析并添加/更新曲目
                if (aPartFiles.Count > 0)
                {
                    OnStatusMessage?.Invoke($"Analyzing {aPartFiles.Count} tracks for changes...");
                    var tracksToSave = new List<MusicTrack>();
                    int processed = 0;

                    foreach (var f in aPartFiles)
                    {
                        try 
                        {
                            long samplesA = GetTotalSamples(f);
                            if (samplesA <= 0) continue;

                            long totalSamples = samplesA;
                            long loopStart = 0;

                            string partB = FindPartB(f);
                            if (!string.IsNullOrEmpty(partB))
                            {
                                long samplesB = GetTotalSamples(partB);
                                if (samplesB > 0)
                                {
                                    totalSamples += samplesB;
                                    loopStart = samplesA; // A/B 模式默认起点
                                }
                            }

                            // 检查数据库里是否已经有了
                            var existingTrack = currentTracks.FirstOrDefault(t => t.FilePath.Equals(f, StringComparison.OrdinalIgnoreCase));
                            
                            // 只有没入库过的，或者是物理状态（长度）发生改变的，才重新入库
                            if (existingTrack == null || existingTrack.TotalSamples != totalSamples)
                            {
                                tracksToSave.Add(new MusicTrack 
                                { 
                                    Id = existingTrack?.Id ?? 0, // 保留原 ID 方便更新
                                    FilePath = f, 
                                    FileName = Path.GetFileName(f), 
                                    TotalSamples = totalSamples,
                                    LoopEnd = totalSamples,
                                    LoopStart = loopStart,
                                    DisplayName = existingTrack?.DisplayName ?? Path.GetFileNameWithoutExtension(f),
                                    LastModified = DateTime.Now
                                });
                            }
                        } 
                        catch { }
                        
                        processed++;
                        if (processed % 10 == 0) OnStatusMessage?.Invoke($"Analyzing... ({processed}/{aPartFiles.Count})");
                    }

                    if (tracksToSave.Count > 0)
                    {
                        _dbHelper.BulkSaveTracksAndAddToPlaylist(tracksToSave, playlistId);
                    }
                }

                // B. 移除消失的歌
                if (tracksToRemove.Count > 0)
                {
                    foreach (var songId in tracksToRemove)
                    {
                        _dbHelper.RemoveSongFromPlaylist(playlistId, songId);
                    }
                }

                OnStatusMessage?.Invoke($"Refresh complete. Analyzed {aPartFiles.Count} files, removed {tracksToRemove.Count} stale records.");
            });
        }
        
        public List<MusicTrack> LoadPlaylistFromDb(int playlistId)
        {
            return _dbHelper.GetPlaylistTracks(playlistId).ToList();
        }

        public MusicTrack GetStoredTrackInfo(string filePath)
        {
            return _dbHelper.GetTrack(filePath, 0);
        }

        public long GetTotalSamples(string filePath)
        {
            try {
                using (var reader = CreateReader(filePath)) {
                    if (reader == null) return 0;
                    return reader.Length / reader.WaveFormat.BlockAlign; 
                }
            } catch { return 0; }
        }

        private WaveStream CreateReader(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            switch (ext)
            {
                case ".wav": return new WaveFileReader(filePath);
                case ".ogg": return new VorbisWaveReader(filePath);
                case ".mp3": return new Mp3FileReader(filePath);
                default: 
                    try { return new AudioFileReader(filePath); } catch { return null; }
            }
        }

        public void UpdateOfflineTrack(MusicTrack track)
        {
            if (track == null || track.TotalSamples <= 0) return;
            try {
                _dbHelper.SaveTrack(track);
            } catch (Exception ex) {
                OnStatusMessage?.Invoke($"Offline Save Error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _audioLooper?.Dispose();
        }
    }
}
