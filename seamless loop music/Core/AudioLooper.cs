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
        private volatile bool _isEndingNaturally = false;
        private volatile bool _hasLoopedSinceSeek = false;

        private volatile bool _isSeamlessLoopEnabled = true;
        public bool IsSeamlessLoopEnabled
        {
            get => _isSeamlessLoopEnabled;
            set
            {
                _isSeamlessLoopEnabled = value;
                SyncLoopConfig();
            }
        }

        public bool IsABFusionLoaded => _abSeamSample >= 0;

        // 匹配长度配置 (秒)
        public double MatchWindowSize { get; set; } = 1.0; 
        public double MatchSearchRadius { get; set; } = 5.0; 

        private bool _isFeatureLoopEnabled = true; // 默认开启：A/B 模式下只循环 Part B
        public bool IsFeatureLoopEnabled
        {
            get => _isFeatureLoopEnabled;
            set
            {
                _isFeatureLoopEnabled = value;
                SyncLoopConfig();
            }
        }


        // 公开读写接口
        public long LoopStartSample => _loopStartSample;
        public long LoopEndSample => _loopEndSample;
        public long TotalSamples => _totalSamples;
        public int SampleRate => _audioStream?.WaveFormat.SampleRate ?? 44100;

        // 状态回调事件
        public event Action<string> OnStatusChanged;
        public event Action<PlaybackState> OnPlayStateChanged; 
        public event Action<long, int> OnAudioLoaded;
        public event Action OnLoopCycleCompleted;
        public event Action OnTrackEnded;
        public event Action<Exception> OnPlaybackError;

        private string _currentFilePath; 
        private string _partBFilePath;   
        private bool _isLoading = false; 
        private long _abSeamSample = -1; // 记录 A/B 拼接点


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
            SyncLoopConfig();
            
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
        /// 重置为原始 A/B 衔接点
        /// </summary>
        public void ResetABLoopPoints()
        {
            if (_abSeamSample >= 0)
            {
                SetLoopPoints(_abSeamSample, _totalSamples);
                OnStatusChanged?.Invoke("Loop points reset to A/B seam.");
            }
            else
            {
                SetLoopPoints(0, _totalSamples);
                OnStatusChanged?.Invoke("Loop points reset to song start/end.");
            }
        }

        private void SyncLoopConfig()
        {
            if (_audioStream == null || _loopStream == null) return;

            _loopStream.EnableLooping = _isSeamlessLoopEnabled;

            long startSample = _loopStartSample;
            long endSample = _loopEndSample;

            // 特色循环逻辑：关闭特色循环时，变成全曲大循环 (0 -> Total)
            if (!_isFeatureLoopEnabled)
            {
                startSample = 0;
                endSample = _totalSamples;
            }

            _loopStream.LoopStartPosition = startSample * _audioStream.WaveFormat.BlockAlign;
            long endPos = endSample * _audioStream.WaveFormat.BlockAlign;
            if (endSample <= 0 || endPos > _audioStream.Length)
                _loopStream.LoopEndPosition = _audioStream.Length;
            else
                _loopStream.LoopEndPosition = endPos;
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

            // 如果已经在播放，也同步一次配置
            // 改进：如果标记为自然结束，说明已经播完了，必须重新开始播放
            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing && !_isEndingNaturally)
            {
                 SyncLoopConfig();
                 return;
            }

            // 如果是暂停状态，直接恢复播放
            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Paused)
            {
                try
                {
                    SyncLoopConfig();
                    _wavePlayer.Play();
                    OnPlayStateChanged?.Invoke(PlaybackState.Playing);
                    OnStatusChanged?.Invoke("Resuming...");
                    return;
                }
                catch (Exception ex)
                {
                    LogDebug($"Resume failed (device lost?): {ex.Message}. Attempting light recovery...");
                    
                    // 核心改进：轻量级恢复。仅重建播放器实例，不重载流和缓冲区
                    try
                    {
                        float currentVolume = _volume;
                        _wavePlayer?.Dispose();
                        
                        _wavePlayer = new WaveOutEvent
                        {
                            DesiredLatency = 200, 
                            NumberOfBuffers = 3,
                            Volume = currentVolume
                        };
                        _wavePlayer.Init(_bufferedProvider);
                        _wavePlayer.PlaybackStopped += WavePlayer_PlaybackStopped;
                        
                        _wavePlayer.Play();
                        OnPlayStateChanged?.Invoke(PlaybackState.Playing);
                        OnStatusChanged?.Invoke("Playback recovered from driver error.");
                        return;
                    }
                    catch (Exception fatalEx)
                    {
                        LogDebug($"Recovery failed: {fatalEx.Message}");
                        OnStatusChanged?.Invoke($"Critical Error: {fatalEx.Message}");
                        Stop();
                        return;
                    }
                }
            }

            try
            {
                _isEndingNaturally = false;
                
                // 核心修复：如果已存在旧播放器，先彻底释放，防止设备占用冲突
                if (_wavePlayer != null)
                {
                    _wavePlayer.PlaybackStopped -= WavePlayer_PlaybackStopped;
                    _wavePlayer.Dispose();
                    _wavePlayer = null;
                }

                // 全新开始：创建循环流
                _loopStream = new LoopStream(_audioStream, _loopStartSample, _loopEndSample);
                SyncLoopConfig(); // 必须调用同步，以应用特色循环开关等动态配置
                _hasLoopedSinceSeek = false;
                _loopStream.OnLoopCompleted += () =>
                {
                    _hasLoopedSinceSeek = true;
                    OnLoopCycleCompleted?.Invoke();
                };

                // --- 异步缓冲系统配置 ---
                _bufferedProvider = new BufferedWaveProvider(_loopStream.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(3), // 增加到3秒，防止毫秒级的溢出
                    DiscardOnBufferOverflow = true,
                    ReadFully = false
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
                _wavePlayer.PlaybackStopped += WavePlayer_PlaybackStopped;

                // 核心修复：同步预填充缓冲区，确保播放器启动时有数据可读
                // 如果 ReadFully=false 且缓冲区为空，WaveOutEvent 会立即停止
                var preBuffer = new byte[_loopStream.WaveFormat.AverageBytesPerSecond]; // 预填 ~1 秒
                int preRead = _loopStream.Read(preBuffer, 0, preBuffer.Length);
                if (preRead > 0)
                {
                    _bufferedProvider.AddSamples(preBuffer, 0, preRead);
                }

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
            _isEndingNaturally = false;
            if (_wavePlayer == null || _wavePlayer.PlaybackState == PlaybackState.Stopped) return;

            try
            {
                _wavePlayer?.Stop();
            }
            catch (Exception ex)
            {
                LogDebug($"Stop failed: {ex.Message}");
            }
            
            OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
            OnStatusChanged?.Invoke("Stopped.");
        }

        private void WavePlayer_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            // 核心改进：忽略来自旧实例或正在加载时的事件
            if (_isLoading || sender != _wavePlayer) return;

            // 核心健壮性改进：捕获底层异常
            if (e.Exception != null)
            {
                OnPlaybackError?.Invoke(e.Exception);
                OnStatusChanged?.Invoke($"[Critical Error]: {e.Exception.Message}");
                LogDebug($"Playback Error Exception: {e.Exception.Message}");
            }

            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Stopped)
            {
                OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
                OnStatusChanged?.Invoke("Playback stopped.");

                if (_isEndingNaturally)
                {
                    _isEndingNaturally = false;
                    OnTrackEnded?.Invoke();
                }
            }
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
                    if (_audioStream != null && _bufferedProvider != null)
                    {
                        // 1. 获取物理位置和缓冲量
                        long fillerPos = _audioStream.Position;
                        long bufferedBytes = _bufferedProvider.BufferedBytes;
                        long speakerPos = fillerPos - bufferedBytes;
                        
                        // 2. 【逻辑回绕补偿】处理循环导致的过早跳变
                        if (_loopStream != null)
                        {
                            long loopStart = _loopStream.LoopStartPosition;
                            long loopEnd = _loopStream.LoopEndPosition;
                            long loopLen = loopEnd - loopStart;

                            if (loopLen > 0 && _isSeamlessLoopEnabled && _hasLoopedSinceSeek)
                            {
                                // 只有确认至少发生过一次循环跳转后，才需要补偿
                                // 这样在 Intro 阶段（0 → loopStart）不会误触发
                                if (speakerPos < loopStart && fillerPos >= loopStart)
                                {
                                    speakerPos += loopLen;
                                }
                            }
                        }

                        // 3. 处理基础的文件末尾回绕
                        if (speakerPos < 0) 
                        {
                            speakerPos += _audioStream.Length;
                        }

                        // 4. 边界约束
                        if (speakerPos < 0) speakerPos = 0;
                        if (speakerPos > _audioStream.Length) speakerPos = _audioStream.Length;

                        return TimeSpan.FromSeconds((double)speakerPos / _audioStream.WaveFormat.AverageBytesPerSecond);
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
                _isEndingNaturally = false;
                lock (_streamLock) // 占位锁，确保此时后台没有在 Read
                {
                    _isSeeking = true;
                    _bufferedProvider.ClearBuffer(); 
                    Interlocked.Exchange(ref _totalBytesReadSinceSeek, 0);
                    _hasLoopedSinceSeek = false;

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
            _isEndingNaturally = false;
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
