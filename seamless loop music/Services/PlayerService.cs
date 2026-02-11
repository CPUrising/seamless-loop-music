using System;
using System.IO;
using NAudio.Wave;
using seamless_loop_music; // AudioLooper 在这里
using seamless_loop_music.Data;
using seamless_loop_music.Models;
using NAudio.Vorbis;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 核心播放业务逻辑服务
    /// 把 MainWindow 从繁重的 Audio+DB 协调工作中解放出来
    /// </summary>
    public class PlayerService : IDisposable
    {
        private readonly AudioLooper _audioLooper;
        private readonly DatabaseHelper _dbHelper;
        
        // 当前正在操作的音乐对象（包含 ID、路径、元数据）
        public MusicTrack CurrentTrack { get; private set; }

        public event Action<MusicTrack> OnTrackLoaded;
        public event Action<PlaybackState> OnPlayStateChanged;
        public event Action<string> OnStatusMessage; // 统一的消息通知

        public PlayerService()
        {
            _audioLooper = new AudioLooper();
            _dbHelper = new DatabaseHelper();

            // 转发底层事件
            _audioLooper.OnPlayStateChanged += state => OnPlayStateChanged?.Invoke(state);
            _audioLooper.OnStatusChanged += msg => OnStatusMessage?.Invoke(msg);
            
            // 核心加载回调：当音频加载完成，立即进行数据库匹配和数据组装
            _audioLooper.OnAudioLoaded += HandleAudioLoaded;
        }

        // --- 核心业务逻辑 ---

        /// <summary>
        /// 加载并初始化一首曲目 (整合了文件加载 + 数据库查询)
        /// </summary>
        public void LoadTrack(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            
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
                // 注意：新朋友暂时不入库，等改名或调整循环点时再入库，或者为了ID稳定现在就入库也可以
                // 但为了保持原有的“无操作不打扰”逻辑，这里暂不强制 SaveTrack
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
            // 可选：匹配完自动保存? 
            // SaveCurrentTrack(); 
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

        public void ImportTracks(System.Collections.Generic.IEnumerable<MusicTrack> tracks)
        {
            _dbHelper.BulkInsert(tracks);
        }

        /// <summary>
        /// 仅用于列表显示：快速查询数据库中是否有该文件的记录（主要为了获取别名）
        /// 不需要加载音频
        /// </summary>
        public MusicTrack GetStoredTrackInfo(string filePath)
        {
            return _dbHelper.GetTrack(filePath, 0);
        }


        /// <summary>
        /// 离线获取音频文件的总采样数 (不播放)
        /// 关键修复：必须使用和 AudioLooper 完全一致的 Reader 创建逻辑
        /// 否则 Mp3FileReader 和 AudioFileReader 计算出的 TotalSamples 可能有微小差异，导致数据库指纹匹配失败
        /// </summary>
        public long GetTotalSamples(string filePath)
        {
            try {
                using (var reader = CreateReader(filePath)) {
                    if (reader == null) return 0;
                    // BlockAlign = Channels * (BitsPerSample / 8)
                    // Length is bytes. TotalSamples = Length / BlockAlign.
                    return reader.Length / reader.WaveFormat.BlockAlign; 
                }
            } catch { return 0; }
        }

        /// <summary>
        /// 辅助方法：创建对应格式的音频流 (复刻 AudioLooper 的逻辑)
        /// </summary>
        private WaveStream CreateReader(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            switch (ext)
            {
                case ".wav": return new WaveFileReader(filePath);
                case ".ogg": return new VorbisWaveReader(filePath);
                case ".mp3": return new Mp3FileReader(filePath);
                default: 
                    // 对于其他格式，尝试用通用读取器
                    try { return new AudioFileReader(filePath); } catch { return null; }
            }
        }

        /// <summary>
        /// 强制保存一个不在当前播放状态的音轨 (离线更新)
        /// </summary>
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
