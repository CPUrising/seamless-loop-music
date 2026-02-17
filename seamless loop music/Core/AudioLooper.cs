using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    /// <summary>
    /// 无缝循环音频播放器 (主控部分)
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


        // 公开读取接口
        public long LoopStartSample => _loopStartSample;
        public long LoopEndSample => _loopEndSample;
        public int SampleRate => _audioStream?.WaveFormat.SampleRate ?? 44100;

        // 状态回调事件
        public event Action<string> OnStatusChanged;
        public event Action<PlaybackState> OnPlayStateChanged; 
        public event Action<long, int> OnAudioLoaded;
        public event Action OnLoopCycleCompleted;

        private string _currentFilePath; 
        private string _partBFilePath;   
        private bool _isLoading = false; 


        /// <summary>
        /// 设置循环起始采样数
        /// </summary>
        public void SetLoopStartSample(long sample)
        {
            if (sample < 0 || sample >= _totalSamples)
            {
                OnStatusChanged?.Invoke($"采样数超出范围!有效范围:0 ~ {_totalSamples - 1}");
                return;
            }
            _loopStartSample = sample;
            if (_loopStream != null)
                _loopStream.LoopStartPosition = sample * _audioStream.WaveFormat.BlockAlign;
            
            OnStatusChanged?.Invoke($"Loop Start set: {sample} ({sample / (double)_audioStream.WaveFormat.SampleRate:F2}s)");
        }

        /// <summary>
        /// 设置循环结束采样数
        /// </summary>
        public void SetLoopEndSample(long sample)
        {
            if (sample < 0)
            {
                sample = 0; // 0 表示末尾
            }
            // 如果非0且小于起点，提示错误（简单校验，具体逻辑让UI层或LoopStream处理）
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
            }
            string endLabel = sample == 0 ? "End of file" : sample.ToString();
            OnStatusChanged?.Invoke($"Loop End set: {endLabel}");
        }

        /// <summary>
        /// 开始播放 (支持从暂停处继续)
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
                    BufferDuration = TimeSpan.FromSeconds(1), // 缩小到 1 秒，平衡稳定性与同步性
                    DiscardOnBufferOverflow = true
                };

                // 创建播放器
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 100, // 恢复 100ms
                    NumberOfBuffers = 2
                };

                _wavePlayer.Init(_bufferedProvider);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    if (_isLoading) return;
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
                    // 修正：实际播放进度 = 已读取并填充的位置 - 还在缓冲区没被硬件吃掉的字节
                    long actualPos = _loopStream.Position - _bufferedProvider.BufferedBytes;
                    if (actualPos < 0) actualPos = 0;
                    return TimeSpan.FromSeconds((double)actualPos / _audioStream.WaveFormat.AverageBytesPerSecond);
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

        public void SeekToSample(long sample)
        {
            if (_loopStream != null && _audioStream != null && _bufferedProvider != null)
            {
                _isSeeking = true;
                _bufferedProvider.ClearBuffer(); 

                long position = sample * _audioStream.WaveFormat.BlockAlign;
                if (position < 0) position = 0;
                long totalLen = _loopStream.Length;
                if (position > totalLen) position = totalLen;
                
                _loopStream.Position = position;
                _isSeeking = false;
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
        }

        public void Dispose()
        {
            Stop();
            DisposeAudioResources();
        }
    }
}
