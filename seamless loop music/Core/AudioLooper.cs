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


        // 公开读取接口
        public long LoopStartSample => _loopStartSample;
        public long LoopEndSample => _loopEndSample;
        public int SampleRate => _audioStream?.WaveFormat.SampleRate ?? 44100;

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
                    OnStatusChanged?.Invoke("Unsupported audio format! Only WAV/OGG/MP3 are supported.");
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
                OnStatusChanged?.Invoke("Audio loaded. Set loop points and play!");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Load failed: {ex.Message}");
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
                        OnStatusChanged?.Invoke("Playback stopped.");
                    }
                };

                _wavePlayer.Play();
                OnPlayStateChanged?.Invoke(PlaybackState.Playing);
                OnStatusChanged?.Invoke("Loop playback started...");
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
        /// 获取当前播放状态
        /// </summary>
        public PlaybackState PlaybackState => _wavePlayer?.PlaybackState ?? PlaybackState.Stopped;

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
            if (_audioStream == null) return;
            
            if (_loopStream == null) {
                Play();
                Pause();
            }

            if (_loopStream != null) {
                double p = Math.Max(0, Math.Min(1.0, percent));
                // 统一使用采样数进行跳转计算，避免字节偏移误差
                long targetSample = (long)(_totalSamples * p);
                SeekToSample(targetSample);
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
        /// 智能寻找最佳循环点 (基于回溯匹配算法 - 逆向模式)
        /// 原理：提取 END 点之前的 1 秒音频作为“指纹”，在 START 点附近寻找完全一致的“指纹”。
        /// 锚定 End 点不动，调整 Start 点以匹配 End 的前导波形。
        /// </summary>
        public void FindBestLoopPoints(long currentStart, long currentEnd, out long bestStart, out long bestEnd)
        {
            bestStart = currentStart; // 默认如果不匹配则不改动
            bestEnd = currentEnd;     // End 是锚点，绝对不动

            if (_audioStream == null) return;

            var fmt = _audioStream.WaveFormat;
            int sampleRate = fmt.SampleRate;

            // 1. 定义匹配窗口 (1秒)
            int windowSize = sampleRate; 

            // 2. 提取 END 点的“前世指纹” (Template)
            // End 点是确定的，我们看看 End 之前听到了什么。
            long templateEndPos = currentEnd;
            long templateStartPos = currentEnd - windowSize;

            // 边界检查
            if (templateStartPos < 0) templateStartPos = 0;
            
            long templateLen = templateEndPos - templateStartPos;
            if (templateLen < sampleRate / 10) return; // 指纹太短

            // 读取 End 前面的波形数据 -> Template
            float[] template = ReadSamples(templateStartPos, (int)templateLen);

            // 3. 定义 START 点的搜索区域 (Search Area)
            // 我们在 Start 点附近 (比如前后 2 秒) 寻找谁也发出了这个声音
            long searchRadius = sampleRate * 2; 
            long searchRegionBegin = currentStart - searchRadius;
            long searchRegionEnd = currentStart + searchRadius;

            // 边界检查
            if (searchRegionBegin < 0) searchRegionBegin = 0;
            if (searchRegionEnd > _totalSamples) searchRegionEnd = _totalSamples;

            long searchLen = searchRegionEnd - searchRegionBegin;
            if (searchLen < templateLen) return;

            // 读取搜索区波形
            float[] searchBuffer = ReadSamples(searchRegionBegin, (int)searchLen);

            if (template.Length == 0 || searchBuffer.Length == 0) return;

            // 4. 核心匹配逻辑 (SAD)
            // 拿着 End 的指纹，在 Start 附近滑动
            double minDiff = double.MaxValue;
            int bestMatchOffset = -1;

            for (int i = 0; i <= searchBuffer.Length - template.Length; i++)
            {
                double diff = 0;
                // 优化步长 4
                for (int t = 0; t < template.Length; t += 4)
                {
                    float valA = template[t];        // End 指纹
                    float valB = searchBuffer[i + t]; // Start 附近的波形
                    diff += Math.Abs(valA - valB);
                    if (diff > minDiff) break; 
                }

                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestMatchOffset = i;
                }
            }

            // 5. 应用结果
            // bestMatchOffset 是指纹在 searchBuffer 中的起始位置。
            // 也就是说 searchBuffer[bestMatchOffset] 开始的这段声音，和 End 前面声音一模一样。
            // 那么这段声音结束的地方，对应的就是 Start 点。
            // 也就是 Start 应该设在：片段的结束位置。
            // 片段起始绝对位置 = searchRegionBegin + bestMatchOffset
            // 片段结束绝对位置 = searchRegionBegin + bestMatchOffset + templateLen
            if (bestMatchOffset != -1)
            {
                long matchPosStart = searchRegionBegin + bestMatchOffset;
                long matchPosEnd = matchPosStart + templateLen;

                // 我们将 Start 调整为匹配片段的结束点
                bestStart = matchPosEnd;
                
                OnStatusChanged?.Invoke($"Smart Match (Reverse): Diff={minDiff:F4}, Shifted Start by {(bestStart - currentStart)} samples.");
            }
        }

        private float[] ReadSamples(long startSample, int count)
        {
            if (_audioStream == null) return new float[0];

            int bytesPerSample = _audioStream.WaveFormat.BlockAlign;
            int bitsPerSample = _audioStream.WaveFormat.BitsPerSample;
            byte[] raw = new byte[count * bytesPerSample];

            // 保存当前流位置用于恢复
            long oldPos = _audioStream.Position;
            
            _audioStream.Position = startSample * bytesPerSample;
            int bytesRead = _audioStream.Read(raw, 0, raw.Length);
            
            _audioStream.Position = oldPos; // 恢复位置

            int samplesRead = bytesRead / bytesPerSample;
            float[] samples = new float[samplesRead];

            // 简单的 PCM 转 Float 解析
            // 注意：这里只取第一个声道做匹配即可，为了速度。如果是立体声，取左声道。
            for (int i = 0; i < samplesRead; i++)
            {
                if (bitsPerSample == 16)
                {
                    short s = BitConverter.ToInt16(raw, i * bytesPerSample);
                    samples[i] = s / 32768f;
                }
                else if (bitsPerSample == 32)
                {
                    samples[i] = BitConverter.ToSingle(raw, i * bytesPerSample);
                }
                // 暂不处理8bit或24bit，通常够用
            }

            return samples;
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
