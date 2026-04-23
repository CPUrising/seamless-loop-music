using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;
using System.Threading;

namespace seamless_loop_music
{
    /// <summary>
    /// 无缝循环音频播放器（主体部分）
    /// </summary>
    public partial class AudioLooper : IDisposable
    {
        private IWavePlayer _wavePlayer;
        private WaveStream _audioStream;
        private LoopStream _loopStream;
        private long _loopStartSample;
        private long _loopEndSample = 0; 
        private long _totalSamples;
        private BufferedWaveProvider _bufferedProvider;
        private System.Threading.CancellationTokenSource _fillerCts;
        private volatile bool _isSeeking = false;
        public bool IsSeeking => _isSeeking;

        // 匹配长度配置 (秒)
        public double MatchWindowSize { get; set; } = 1.0; 
        public double MatchSearchRadius { get; set; } = 5.0; 


        // 公开读写接口
        public long LoopStartSample => _loopStartSample;
        public long LoopEndSample => _loopEndSample;
        public int SampleRate => _audioStream?.WaveFormat.SampleRate ?? 44100;

        // 状态回调事件
        public event Action<string> OnStatusChanged;
        public event Action<PlaybackState> OnPlayStateChanged; 
        public event Action<long, int> OnAudioLoaded;
        public event Action OnLoopCycleCompleted;
        public event Action<Exception> OnPlaybackError;

        private string _currentFilePath; 
        private string _partBFilePath;   
        private bool _isLoading = false; 


        /// <summary>
        /// 设置循环起始采样点
        /// </summary>
        public void SetLoopStartSample(long sample)
        {
            if (sample < 0 || sample >= _totalSamples)
            {
                OnStatusChanged?.Invoke($"采样点超出有效范围:0 ~ {_totalSamples - 1}");
                return;
            }
            _loopStartSample = sample;
            if (_loopStream != null)
            {
                _loopStream.LoopStartPosition = sample * _audioStream.WaveFormat.BlockAlign;
                // 注意：这里不强制 ClearBuffer，因为起点变化通常不影响当前正在播放的片段
                // 只有当起点被设为当前点之后（比如往前跳了）才需要考虑，但为了稳定最好不 Clear
            }
            
            OnStatusChanged?.Invoke($"Loop Start set: {sample} ({sample / (double)_audioStream.WaveFormat.SampleRate:F2}s)");
        }

        /// <summary>
        /// 设置循环结束采样点
        /// </summary>
        public void SetLoopEndSample(long sample)
        {
            if (sample < 0)
            {
                sample = 0; // 0 表示文件末尾
            }
            // 如果小于等于起点，提示错误（简单校验，具体逻辑由UI层或LoopStream处理）
            if (sample > 0 && sample <= _loopStartSample)
            {
                OnStatusChanged?.Invoke($"错误: 循环终点必须大于起点({_loopStartSample})");
                return;
            }
            
            _loopEndSample = sample;
            if (_loopStream != null)
            {
                 long endPos = sample * _audioStream.WaveFormat.BlockAlign;
                 _loopStream.LoopEndPosition = (sample <= 0 || endPos > _audioStream.Length) ? _audioStream.Length : endPos;
                 
                 // 重点改进：如果新的循环终点在当前位置之前，必须立即清除缓冲并 Seek
                 // 否则用户会听到已经被截掉的音乐
                 if (sample > 0 && CurrentTime.TotalSeconds * SampleRate > sample)
                 {
                     SeekToSample(_loopStartSample); 
                 }
            }
            string endLabel = sample == 0 ? "End of file" : sample.ToString();
            OnStatusChanged?.Invoke($"Loop End set: {endLabel}");
        }

        public void SetLoopPoints(long startSample, long endSample)
        {
            SetLoopStartSample(startSample);
            SetLoopEndSample(endSample);
        }

        /// <summary>
        /// 开始播放（支持从暂停处继续）
        /// </summary>
        public void Play()
        {
            if (_audioStream == null)
            {
                OnStatusChanged?.Invoke("Playback failed: Audio not loaded!");
                return;
            }

            // 如果是暂停状态，直接恢复播放
            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Paused)
            {
                // 强制同步一次配置，防止意外
                if (_loopStream != null)
                {
                     _loopStream.LoopStartPosition = _loopStartSample * _audioStream.WaveFormat.BlockAlign;
                     // 同步 LoopEnd
                     long endPos = _loopEndSample * _audioStream.WaveFormat.BlockAlign;
                     if (_loopEndSample <= 0 || endPos > _audioStream.Length)
                         _loopStream.LoopEndPosition = _audioStream.Length;
                     else
                         _loopStream.LoopEndPosition = endPos;
                }
                
                _wavePlayer.Play();
                OnPlayStateChanged?.Invoke(PlaybackState.Playing);
                OnStatusChanged?.Invoke("Resuming...");
                return;
            }

            // 如果已经在播放，也同步一次配置? 
            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing)
            {
                 if (_loopStream != null)
                 {
                      _loopStream.LoopStartPosition = _loopStartSample * _audioStream.WaveFormat.BlockAlign;
                      long endPos = _loopEndSample * _audioStream.WaveFormat.BlockAlign;
                      if (_loopEndSample <= 0 || endPos > _audioStream.Length)
                          _loopStream.LoopEndPosition = _audioStream.Length;
                      else
                          _loopStream.LoopEndPosition = endPos;
                 }
                 return;
            }

            try
            {
                // 全新开始：创建循环流
                _loopStream = new LoopStream(_audioStream, _loopStartSample, _loopEndSample);
                _loopStream.OnLoopCompleted += () => OnLoopCycleCompleted?.Invoke();

                // --- 异步缓冲系统配置 ---
                _bufferedProvider = new BufferedWaveProvider(_loopStream.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(3), // 增加到3秒，防止毫秒级的溢出
                    DiscardOnBufferOverflow = true
                };

                // 创建播放器
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 200, 
                    NumberOfBuffers = 3,
                    Volume = _volume
                };
                LogDebug($"WaveOutEvent created with volume {_volume}");

                _wavePlayer.Init(_bufferedProvider);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    if (_isLoading) return;
                    
                    // 核心健壮性改进：捕获底层异常
                    if (e.Exception != null)
                    {
                        OnPlaybackError?.Invoke(e.Exception);
                        OnStatusChanged?.Invoke($"[Critical Error]: {e.Exception.Message}");
                    }
                    
                    if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Stopped)
                    {
                        OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
                        OnStatusChanged?.Invoke("Playback stopped.");
                    }
                };

                // 启动后台填充任务
                StartFillerTask();

                _wavePlayer.Play();
                OnPlayStateChanged?.Invoke(PlaybackState.Playing);
                OnStatusChanged?.Invoke("Stable Loop playback started (Async Buffering Enabled)...");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Error: {ex.Message}");
                Stop();
            }
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        public void Pause()
        {
            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing)
            {
                _wavePlayer.Pause();
                OnPlayStateChanged?.Invoke(PlaybackState.Paused);
                OnStatusChanged?.Invoke("Paused");
            }
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        public void Stop()
        {
            if (_wavePlayer == null || _wavePlayer.PlaybackState == PlaybackState.Stopped) return;

            _wavePlayer?.Stop();
            OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
            OnStatusChanged?.Invoke("Stopped.");
        }

        private float _volume = 1.0f;
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(1.0f, value));
                if (_wavePlayer != null)
                {
                    _wavePlayer.Volume = _volume;
                }
            }
        }

        public PlaybackState PlaybackState => _wavePlayer?.PlaybackState ?? PlaybackState.Stopped;

        public TimeSpan CurrentTime
        {
            get
            {
                lock (_streamLock)
                {
                    if (_loopStream != null && _audioStream != null && _bufferedProvider != null)
                    {
                        long playedBytes = _totalBytesReadSinceSeek - _bufferedProvider.BufferedBytes;
                        if (playedBytes < 0) playedBytes = 0;
                        
                        long playedSamples = playedBytes / _audioStream.WaveFormat.BlockAlign;
                        long currentSample = _seekTargetSample + playedSamples;
                        
                        if (_loopEndSample > 0 && currentSample >= _loopEndSample)
                        {
                             long loopLength = _loopEndSample - _loopStartSample;
                             if (loopLength > 0)
                             {
                                 long overshoot = currentSample - _loopEndSample;
                                 currentSample = _loopStartSample + (overshoot % loopLength);
                             }
                        }
                        else if (_loopEndSample <= 0 && currentSample >= _totalSamples)
                        {
                             long loopLength = _totalSamples - _loopStartSample;
                             if (loopLength > 0)
                             {
                                 long overshoot = currentSample - _totalSamples;
                                 currentSample = _loopStartSample + (overshoot % loopLength);
                             }
                        }

                        return TimeSpan.FromSeconds((double)currentSample / _audioStream.WaveFormat.SampleRate);
                    }
                }
                return TimeSpan.Zero;
            }
        }



        public TimeSpan TotalTime => _audioStream?.TotalTime ?? TimeSpan.Zero;

        public void Seek(double percent)
        {
            if (_audioStream == null) return;
            
            if (_loopStream == null) {
                Play();
                Pause();
            }

            if (_loopStream != null) {
                double p = Math.Max(0, Math.Min(1.0, percent));
                long targetSample = (long)(_totalSamples * p);
                SeekToSample(targetSample);
            }
        }

        private DateTime _seekTime = DateTime.MinValue;
        private long _seekTargetSample = 0;
        private long _totalBytesReadSinceSeek = 0;
        private readonly object _streamLock = new();

        public void SeekToSample(long sample)
        {
            if (_loopStream != null && _audioStream != null && _bufferedProvider != null)
            {
                lock (_streamLock) // 占位锁，确保此时后台没有在 Read
                {
                    _isSeeking = true;
                    _bufferedProvider.ClearBuffer(); 
                    Interlocked.Exchange(ref _totalBytesReadSinceSeek, 0);

                    long position = sample * _audioStream.WaveFormat.BlockAlign;
                    if (position < 0) position = 0;
                    long totalLen = _loopStream.Length;
                    if (position > totalLen) position = totalLen;
                    
                    _loopStream.Position = position;

                    // 记录 Seek 状态，用于平滑 UI 显示
                    _seekTargetSample = sample;
                    _seekTime = DateTime.Now;

                    // 注意：这里不再立即将 _isSeeking 设为 false
                    // 我们让后台填充线程在填入第一帧数据后再将其释放
                }
            }
        }

        private void DisposeAudioResources()
        {
            StopFillerTask();
            _wavePlayer?.Dispose();
            _loopStream = null;
            _audioStream?.Dispose();
            _bufferedProvider = null;
            _wavePlayer = null;
            _audioStream = null;

            // 核心修复：彻底重置进度的相关内部状态，确保新歌加载后计算准确
            _seekTargetSample = 0;
            _isSeeking = false;
            Interlocked.Exchange(ref _totalBytesReadSinceSeek, 0);
        }

        public void Dispose()
        {
            Stop();
            DisposeAudioResources();
        }
        private void LogDebug(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio_debug.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
            OnStatusChanged?.Invoke(message);
        }
    }
}
