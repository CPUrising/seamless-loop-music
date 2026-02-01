using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    /// <summary>
    /// 无缝循环音频播放器
    /// </summary>
    public class AudioLooper : IDisposable
    {
        private IWavePlayer _wavePlayer;
        private WaveStream _audioStream;
        private LoopStream _loopStream;
        private long _loopStartSample;
        private long _loopEndSample = 0; // 新增: 循环结束采样数
        private long _totalSamples;
        private bool _isPlaying;

        // 状态回调事件
        public event Action<string> OnStatusChanged;
        public event Action<PlaybackState> OnPlayStateChanged; // 升级: 传递详细状态
        // 新增: 音频加载完成事件 (总采样数, 采样率)
        public event Action<long, int> OnAudioLoaded;

        /// <summary>
        /// 加载音频文件
        /// </summary>
        public void LoadAudio(string filePath)
        {
            try
            {
                Stop();
                DisposeAudioResources();

                // 根据格式创建音频流
                _audioStream = CreateAudioStream(filePath);
                if (_audioStream == null)
                {
                    OnStatusChanged?.Invoke("不支持的音频格式!仅支持WAV/OGG/MP3");
                    return;
                }

                // 计算音频核心参数
                var waveFormat = _audioStream.WaveFormat;
                int bytesPerSample = waveFormat.BlockAlign;
                _totalSamples = _audioStream.Length / bytesPerSample;

                // 修复: 加载新音频时，强制重置循环点为默认值 (0 ~ Total)
                _loopStartSample = 0;
                _loopEndSample = _totalSamples; // 默认循环全曲

                // 触发加载完成事件
                OnAudioLoaded?.Invoke(_totalSamples, waveFormat.SampleRate);
                OnStatusChanged?.Invoke("音频加载成功! 请设置循环点后播放");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"加载失败:{ex.Message}");
            }
        }

        /// <summary>
        /// 创建对应格式的音频流
        /// </summary>
        private WaveStream CreateAudioStream(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            switch (ext)
            {
                case ".wav":
                    return new WaveFileReader(filePath);
                case ".ogg":
                    return new VorbisWaveReader(filePath);
                case ".mp3":
                    return new Mp3FileReader(filePath);
                default:
                    return null;
            }
        }

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
            // 实时同步更新 LoopStream
            if (_loopStream != null)
            {
                _loopStream.LoopStartPosition = sample * _audioStream.WaveFormat.BlockAlign;
            }
            OnStatusChanged?.Invoke($"循环点已设置:{sample} (对应秒数:{sample / (double)_audioStream.WaveFormat.SampleRate:F2})");
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
             // 实时同步更新 LoopStream
            if (_loopStream != null)
            {
                 // 需要重新计算字节位置，逻辑与 LoopStream 构造里的一致
                 long endPos = sample * _audioStream.WaveFormat.BlockAlign;
                 if (sample <= 0 || endPos > _audioStream.Length)
                 {
                     _loopStream.LoopEndPosition = _audioStream.Length;
                 }
                 else
                 {
                     _loopStream.LoopEndPosition = endPos;
                 }
            }
            string endText = sample == 0 ? "文件末尾" : sample.ToString();
            OnStatusChanged?.Invoke($"循环终点: {endText}");
        }

        /// <summary>
        /// 开始播放 (支持从暂停处继续)
        /// </summary>
        public void Play()
        {
            if (_audioStream == null)
            {
                OnStatusChanged?.Invoke("播放失败:未加载音频!");
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
                OnStatusChanged?.Invoke("继续播放...");
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

                // 创建播放器
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 100,  // 低延迟
                    NumberOfBuffers = 2
                };

                _wavePlayer.Init(_loopStream);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    // 只有非手动暂停导致的停止才触发完全停止逻辑
                    if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Stopped)
                    {
                        OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
                        OnStatusChanged?.Invoke("播放已停止");
                    }
                };

                _wavePlayer.Play();
                OnPlayStateChanged?.Invoke(PlaybackState.Playing);
                OnStatusChanged?.Invoke("开始无缝循环播放...");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"播放异常:{ex.Message}");
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
                OnStatusChanged?.Invoke("已暂停");
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
            OnStatusChanged?.Invoke("已停止播放");
        }

        /// <summary>
        /// 设置音量(0~1)
        /// </summary>
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

        /// <summary>
        /// 获取当前播放时间
        /// </summary>
        public TimeSpan CurrentTime
        {
            get
            {
                if (_loopStream != null)
                {
                    return TimeSpan.FromSeconds((double)_loopStream.Position / _audioStream.WaveFormat.AverageBytesPerSecond);
                }
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 获取音频总时长
        /// </summary>
        public TimeSpan TotalTime
        {
            get
            {
                if (_audioStream != null)
                {
                    return _audioStream.TotalTime;
                }
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 跳转进度 (0.0 ~ 1.0)
        /// </summary>
        public void Seek(double percent)
        {
            if (_loopStream != null)
            {
                double p = percent;
                if (p < 0.0) p = 0.0;
                if (p > 1.0) p = 1.0;
                long position = (long)(_loopStream.Length * p);
                _loopStream.Position = position;
            }
        }

        /// <summary>
        /// 精准跳转到指定采样数
        /// </summary>
        public void SeekToSample(long sample)
        {
            if (_loopStream != null && _audioStream != null)
            {
                long position = sample * _audioStream.WaveFormat.BlockAlign;
                if (position < 0) position = 0;
                if (position > _loopStream.Length) position = _loopStream.Length;
                _loopStream.Position = position;
            }
        }

        /// <summary>
        /// 释放音频资源
        /// </summary>
        private void DisposeAudioResources()
        {
            _wavePlayer?.Dispose();
            _loopStream = null;
            _audioStream?.Dispose();
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
