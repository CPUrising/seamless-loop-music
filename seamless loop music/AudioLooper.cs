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
        private long _totalSamples;
        private bool _isPlaying;

        // 状态回调事件
        public event Action<string> OnStatusChanged;
        public event Action<bool> OnPlayStateChanged;

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

                OnStatusChanged?.Invoke($"音频加载成功!总采样数:{_totalSamples} | 采样率:{waveFormat.SampleRate}Hz");
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
            return ext switch
            {
                ".wav" => new WaveFileReader(filePath),
                ".ogg" => new VorbisWaveReader(filePath),
                ".mp3" => new Mp3FileReader(filePath),
                _ => null
            };
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
            OnStatusChanged?.Invoke($"循环点已设置:{sample} (对应秒数:{sample / (double)_audioStream.WaveFormat.SampleRate:F2})");
        }

        /// <summary>
        /// 开始无缝循环播放
        /// </summary>
        public void Play()
        {
            if (_isPlaying || _audioStream == null)
            {
                OnStatusChanged?.Invoke("播放失败:未加载音频!");
                return;
            }

            try
            {
                // 创建循环流
                _loopStream = new LoopStream(_audioStream, _loopStartSample);

                // 创建播放器
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 100,  // 低延迟
                    NumberOfBuffers = 2
                };

                _wavePlayer.Init(_loopStream);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    if (_isPlaying)
                    {
                        _isPlaying = false;
                        OnPlayStateChanged?.Invoke(false);
                        OnStatusChanged?.Invoke("播放已停止");
                    }
                };

                _wavePlayer.Play();
                _isPlaying = true;
                OnPlayStateChanged?.Invoke(true);
                OnStatusChanged?.Invoke("开始无缝循环播放...");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"播放异常:{ex.Message}");
                Stop();
            }
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        public void Stop()
        {
            if (!_isPlaying) return;

            _isPlaying = false;
            _wavePlayer?.Stop();
            OnPlayStateChanged?.Invoke(false);
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
                    _wavePlayer.Volume = Math.Clamp(value, 0.0f, 1.0f);
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
