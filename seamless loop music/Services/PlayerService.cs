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
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            
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
        /// 智能匹配循环点
        /// </summary>
        public void SmartMatchLoop()
        {
            if (CurrentTrack == null) return;
            
            long start = _audioLooper.LoopStartSample;
            long end = _audioLooper.LoopEndSample;
            
            _audioLooper.FindBestLoopPoints(start, end, out long newStart, out long newEnd);
            
            // 应用结果
            _audioLooper.SetLoopStartSample(newStart);
            _audioLooper.SetLoopEndSample(newEnd);
            
            OnStatusMessage?.Invoke("Smart Match applied.");
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

        public void CreatePlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            _dbHelper.AddPlaylist(name, folderPath, isLinked);
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

        public void RemoveTrackFromPlaylist(int playlistId, MusicTrack track)
        {
            if (track.Id > 0)
            {
                _dbHelper.RemoveSongFromPlaylist(playlistId, track.Id);
            }
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
