using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;
using System.Threading;

namespace seamless_loop_music
{
    /// <summary>
    /// 无缝循环音频播放�?(主控部分)
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

        // 匹配长度配置 (�?
        public double MatchWindowSize { get; set; } = 1.0; 
        public double MatchSearchRadius { get; set; } = 5.0; 


        // 公开读取接口
        public long LoopStartSample => _loopStartSample;
        public long LoopEndSample => _loopEndSample;
        public int SampleRate => _audioStream?.WaveFormat.SampleRate ?? 44100;

        // 状态回调事�?
        public event Action<string> OnStatusChanged;
        public event Action<PlaybackState> OnPlayStateChanged; 
        public event Action<long, int> OnAudioLoaded;
        public event Action OnLoopCycleCompleted;
        public event Action<TimeSpan> OnPositionChanged;
        public event Action<Exception> OnPlaybackError;

        private string _currentFilePath; 
        private string _partBFilePath;   
        private bool _isLoading = false; 


        /// <summary>
        /// 设置循环起始采样�?
        /// </summary>
        public void SetLoopStartSample(long sample)
        {
            if (sample < 0 || sample >= _totalSamples)
            {
                OnStatusChanged?.Invoke($"采样数超出范�?有效范围:0 ~ {_totalSamples - 1}");
                return;
            }
            _loopStartSample = sample;
            if (_loopStream != null)
            {
                _loopStream.LoopStartPosition = sample * _audioStream.WaveFormat.BlockAlign;
                // 注意：这里不强制 ClearBuffer，因为起始点变动通常不影响当前正在播放的片段
                // 只有当起始点被设为当前点之后（比如往前挪了）才需要考虑，但为了稳定暂不 Clear
            }
            
            OnStatusChanged?.Invoke($"Loop Start set: {sample} ({sample / (double)_audioStream.WaveFormat.SampleRate:F2}s)");
        }

        /// <summary>
        /// 设置循环结束采样�?
        /// </summary>
        public void SetLoopEndSample(long sample)
        {
            if (sample < 0)
            {
                sample = 0; // 0 表示末尾
            }
            // 如果�?且小于起点，提示错误（简单校验，具体逻辑让UI层或LoopStream处理�?
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
                 
                 // 重点改进：如果新的循环终点在当前播放点之前，必须立即清除缓冲区并 Seek
                 // 否则用户会听到已经填入缓冲区的、本该被切掉的音�?
                 if (sample > 0 && CurrentTime.TotalSeconds * SampleRate > sample)
                 {
                     SeekToSample(_loopStartSample); 
                 }
            }
            string endLabel = sample == 0 ? "End of file" : sample.ToString();
            OnStatusChanged?.Invoke($"Loop End set: {endLabel}");
        }

        /// <summary>
        /// 开始播�?(支持从暂停处继续)
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

            // 如果已经在播放，也同步一次配�? 
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
                // 全新开始：创建循环�?
                _loopStream = new LoopStream(_audioStream, _loopStartSample, _loopEndSample);
                _loopStream.OnLoopCompleted += () => OnLoopCycleCompleted?.Invoke();

                // --- 异步缓冲系统配置 ---
                _bufferedProvider = new BufferedWaveProvider(_loopStream.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(2), // 降低�?2秒，提高响应速度
                    DiscardOnBufferOverflow = true
                };

                // 创建播放�?
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 200, // 稍微放松到底�?200ms，大幅减少爆音概�?
                    NumberOfBuffers = 3  // 增加缓冲块数�?
                };

                _wavePlayer.Init(_bufferedProvider);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    if (_isLoading) return;
                    
                    // 核心稳健性改进：捕获底层异常
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

        public float Volume
        {
            get => _wavePlayer?.Volume ?? 1.0f;
            set
            {
                if (_wavePlayer != null)
                {
                    float val = value;
                    if (val < 0.0f) val = 0.0f;
                    if (val > 1.0f) val = 1.0f;
                    _wavePlayer.Volume = val;
                }
            }
        }

        public PlaybackState PlaybackState => _wavePlayer?.PlaybackState ?? PlaybackState.Stopped;

        public TimeSpan CurrentTime
        {
            get
            {
                if (_loopStream != null && _audioStream != null && _bufferedProvider != null)
                {
                    // 修正：实际播放进�?= 上次Seek目标 + 实际已播放的采样�?
                    // 实际已播放层�?从上次定位填入缓冲区的总字节数 - 在缓冲区等待播放的字节数)
                    // 放弃直接使用�?Position，防止在循环边界由于底层瞬间跳转而导致的回退
                    long playedBytes = Interlocked.Read(ref _totalBytesReadSinceSeek) - _bufferedProvider.BufferedBytes;
                    if (playedBytes < 0) playedBytes = 0;
                    
                    long playedSamples = playedBytes / _audioStream.WaveFormat.BlockAlign;
                    long currentSample = _seekTargetSample + playedSamples;
                    
                    // 处理循环回绕现象：当前估算采样数超过循环结束点必定折�?
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
                         // 代表一直播到文件末尾才跳回的边界情�?
                         long loopLength = _totalSamples - _loopStartSample;
                         if (loopLength > 0)
                         {
                             long overshoot = currentSample - _totalSamples;
                             currentSample = _loopStartSample + (overshoot % loopLength);
                         }
                    }

                    return TimeSpan.FromSeconds((double)currentSample / _audioStream.WaveFormat.SampleRate);
                }
                return TimeSpan.Zero;
            }
        }

        private DateTime _lastPositionNotifyTime = DateTime.MinValue;
        private void CheckAndNotifyPosition()
        {
            if (OnPositionChanged != null && (DateTime.Now - _lastPositionNotifyTime).TotalMilliseconds >= 100)
            {
                _lastPositionNotifyTime = DateTime.Now;
                OnPositionChanged.Invoke(CurrentTime);
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
                lock (_streamLock) // 抢占锁，确保此时后台没有�?Read
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
                    // 我们让后台填充线程在填入第一批数据后再将其释�?
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

            // 核心修复：彻底重置进度追踪相关的内部状态，确保新歌加载后计算准�?
            _seekTargetSample = 0;
            _isSeeking = false;
            Interlocked.Exchange(ref _totalBytesReadSinceSeek, 0);
        }

        public void Dispose()
        {
            Stop();
            DisposeAudioResources();
        }
    }
}

