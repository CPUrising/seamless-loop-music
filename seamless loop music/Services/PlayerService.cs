using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using NAudio.Vorbis;

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
        private readonly Random _random = new Random();
        
        // 播放相关
        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        public int CurrentIndex { get; set; } = -1;
        public PlayMode CurrentMode { get; set; } = PlayMode.SingleLoop;
        public int LoopLimit { get; set; } = 1; // 列表模式下，每首歌循环几次后切换？
        private int _currentLoopCount = 0;

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
            
            // 关键修复：每次加载新歌，都试图同步索引。这样手动点选和列表播放就能合拍了
            if (Playlist != null)
            {
                int newIndex = Playlist.FindIndex(t => t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                if (newIndex != -1)
                {
                    CurrentIndex = newIndex;
                    OnIndexChanged?.Invoke(newIndex); // 让 UI 跟着亮起来
                }
            }
            
            _currentLoopCount = 0; // 只要换了歌，循环计数就归零

            // 1. 让 Looper 加载音频 (它成功后会触发 OnAudioLoaded -> HandleAudioLoaded)
            CurrentTrack = new MusicTrack { FilePath = filePath, FileName = Path.GetFileName(filePath) }; // 临时占位
            _audioLooper.LoadAudio(filePath); 
        }

        /// <summary>
        /// 处理音频加载完成后的数据整合
        /// </summary>
        private void HandleAudioLoaded(long totalSamples, int sampleRate)
        {
            if (CurrentTrack == null) return;

            CurrentTrack.TotalSamples = totalSamples;
            
            // 2. 去数据库查户口 (通过 文件名+采样数 指纹)
            var dbTrack = _dbHelper.GetTrack(CurrentTrack.FilePath, totalSamples);
            
            if (dbTrack != null)
            {
                // 老朋友：完全接管 ID 和配置
                CurrentTrack.Id = dbTrack.Id;
                CurrentTrack.DisplayName = dbTrack.DisplayName;
                CurrentTrack.LoopStart = dbTrack.LoopStart;
                CurrentTrack.LoopEnd = (dbTrack.LoopEnd <= 0) ? totalSamples : dbTrack.LoopEnd;
            }
            else
            {
                // 新朋友：初始化默认循环点
                CurrentTrack.LoopStart = 0;
                CurrentTrack.LoopEnd = totalSamples;
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

                // 4. 找出新增的文件
                var newFiles = disksFilesMap.Keys.Where(f => !currentTrackPaths.Contains(f)).ToList();
                
                // 5. 找出移除的文件 (只移除那些路径在 monitored folders 范围内但不复存在的文件？
                // 或者更简单：如果歌单是 "Linked Folder" 模式，则完全同步？
                // 鉴于用户需求是 "根据文件夹变化决定歌单变化"，我们假设这是同步模式。
                // 但为了安全，我们只移除那些 "原本属于这些文件夹但现在不见了" 的歌曲，
                // 避免误删用户手动添加的额外歌曲（如果支持混合模式）。
                // 简化起见：我们只移除那些 路径以任何一个 folder 开头 但不在 diskFilesMap 中的歌曲。
                
                var tracksToRemove = new List<int>();
                foreach (var track in currentTracks)
                {
                    // 如果歌曲路径属于某个受控文件夹
                    bool belongsToMonitored = folders.Any(folder => track.FilePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
                    
                    if (belongsToMonitored)
                    {
                        // 且现在磁盘上找不到了
                        if (!disksFilesMap.ContainsKey(track.FilePath))
                        {
                            tracksToRemove.Add(track.Id);
                        }
                    }
                }

                // --- 执行数据库更新 ---

                // A. 添加新歌
                if (newFiles.Count > 0)
                {
                    OnStatusMessage?.Invoke($"Found {newFiles.Count} new files. analyzing...");
                    var tracksToAdd = new List<MusicTrack>();
                    int processed = 0;

                    foreach (var f in newFiles)
                    {
                        try 
                        {
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
                        catch { }
                        
                        processed++;
                        if (processed % 10 == 0) OnStatusMessage?.Invoke($"Analyzing... ({processed}/{newFiles.Count})");
                    }

                    if (tracksToAdd.Count > 0)
                    {
                        _dbHelper.BulkSaveTracksAndAddToPlaylist(tracksToAdd, playlistId);
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

                OnStatusMessage?.Invoke($"Refresh complete. +{newFiles.Count} / -{tracksToRemove.Count} songs.");
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
