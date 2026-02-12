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
        // 新增: 循环完成一次事件
        public event Action OnLoopCycleCompleted;

        private string _currentFilePath; // 新增: 记录当前文件路径，用于异步分析
        private bool _isLoading = false; // 新增: 防止自动切歌时 Stopped 事件干扰 UI


        /// <summary>
        /// 加载音频文件
        /// </summary>
        public void LoadAudio(string filePath)
        {
            try
            {
                _isLoading = true; // 标记开始加载，屏蔽旧的停止事件
                Stop();
                DisposeAudioResources();
                
                
                _currentFilePath = filePath; // 记录路径

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
            finally
            {
                _isLoading = false; 
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
                _loopStream.OnLoopCompleted += () => OnLoopCycleCompleted?.Invoke();

                // 创建播放器
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 100,  // 低延迟
                    NumberOfBuffers = 2
                };

                _wavePlayer.Init(_loopStream);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    // 如果正在加载新歌，忽略旧播放器的停止事件
                    if (_isLoading) return;

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
                if (_loopStream != null && _audioStream != null)
                {
                    return TimeSpan.FromSeconds((double)_loopStream.Position / _audioStream.WaveFormat.AverageBytesPerSecond);
                }
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 获取音频总时长
        /// </summary>
        public TimeSpan TotalTime => _audioStream?.TotalTime ?? TimeSpan.Zero;

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
        /// 智能寻找最佳循环点 (基于金字塔搜索 Pyramid Search)
        /// 优化后：先降采样粗搜，再局部精搜，速度大幅提升。
        /// </summary>

        /// <summary>
        /// 智能寻找最佳循环点 (异步版 - 基于金字塔搜索 Pyramid Search)
        /// 使用独立文件流，避免与播放线程冲突。
        /// </summary>
        /// <param name="adjustStart">True: 固定 End 找 Start (逆向); False: 固定 Start 找 End (正向)</param>
        public async void FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart, Action<long, long> onResult)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                OnStatusChanged?.Invoke("Error: File path not available for analysis.");
                return;
            }

            // 保存路径，避免闭包问题
            string targetPath = _currentFilePath;
            
            await System.Threading.Tasks.Task.Run(() =>
            {
                WaveStream tempStream = null;
                try
                {
                    // 1. 创建独立的临时流，避免干扰播放
                    tempStream = CreateAudioStream(targetPath);
                    if (tempStream == null) return;

                    var fmt = tempStream.WaveFormat;
                    int sampleRate = fmt.SampleRate;
                    long totalSamples = tempStream.Length / fmt.BlockAlign;

                    // --- 参数准备 ---
                    int windowSize = sampleRate; 
                    
                    // 动态限制指纹长度
                    if (adjustStart) { // 逆向
                         if (currentEnd < windowSize) windowSize = (int)currentEnd;
                    } else { // 正向
                         if (currentStart + windowSize > totalSamples) windowSize = (int)(totalSamples - currentStart);
                    }
                    if (windowSize < sampleRate / 20) return; 

                    long searchRadius = sampleRate * 5; 
                    
                    // --- 提取波形 & 确定搜索区 ---
                    long templateStartPos, templateEndPos;
                    long searchRegionCenter;

                    if (adjustStart)
                    {
                        // 逆向 (Reverse): 指纹 = [End - 1s, End]
                        templateEndPos = currentEnd;
                        templateStartPos = templateEndPos - windowSize;
                        searchRegionCenter = currentStart;
                    }
                    else
                    {
                        // 正向 (Forward): 指纹 = [Start, Start + 1s]
                        templateStartPos = currentStart;
                        templateEndPos = templateStartPos + windowSize;
                        searchRegionCenter = currentEnd;
                    }

                    if (templateStartPos < 0) templateStartPos = 0;
                    long templateLen = templateEndPos - templateStartPos;

                    long searchRegionBegin = searchRegionCenter - searchRadius;
                    long searchRegionEnd = searchRegionCenter + searchRadius;
                    
                    if (searchRegionBegin < 0) searchRegionBegin = 0;
                    if (searchRegionEnd > totalSamples) searchRegionEnd = totalSamples;

                    long searchLen = searchRegionEnd - searchRegionBegin;
                    if (searchLen < templateLen) return;

                    // 从临时流读取数据
                    float[] templateFull = ReadSamplesFromStream(tempStream, templateStartPos, (int)templateLen);
                    float[] searchBufferFull = ReadSamplesFromStream(tempStream, searchRegionBegin, (int)searchLen);

                    if (templateFull.Length < 100 || searchBufferFull.Length < templateFull.Length) return;

                    // --- 第一层：金字塔粗搜 ---
                    int downsampleFactor = 8;
                    float[] templateSmall = Downsample(templateFull, downsampleFactor);
                    float[] searchSmall = Downsample(searchBufferFull, downsampleFactor);

                    int bestCoarseOffset = -1;
                    double minCoarseDiff = double.MaxValue;

                    for (int i = 0; i <= searchSmall.Length - templateSmall.Length; i++)
                    {
                        double diff = 0;
                        for (int t = 0; t < templateSmall.Length; t++)
                        {
                            diff += Math.Abs(templateSmall[t] - searchSmall[i + t]);
                            if (diff > minCoarseDiff) break;
                        }
                        if (diff < minCoarseDiff)
                        {
                            minCoarseDiff = diff;
                            bestCoarseOffset = i;
                        }
                    }

                    if (bestCoarseOffset == -1) return;

                    // --- 第二层：局部精搜 ---
                    int fineSearchRadius = downsampleFactor * 4;
                    int fineStart = bestCoarseOffset * downsampleFactor - fineSearchRadius;
                    int fineEnd = bestCoarseOffset * downsampleFactor + fineSearchRadius;

                    if (fineStart < 0) fineStart = 0;
                    if (fineEnd > searchBufferFull.Length - templateFull.Length) 
                        fineEnd = searchBufferFull.Length - templateFull.Length;

                    double minFineDiff = double.MaxValue;
                    int bestFineOffset = -1;

                    for (int i = fineStart; i <= fineEnd; i++)
                    {
                        double diff = 0;
                        for (int t = 0; t < templateFull.Length; t += 2) 
                        {
                            diff += Math.Abs(templateFull[t] - searchBufferFull[i + t]);
                            if (diff > minFineDiff) break;
                        }
                        if (diff < minFineDiff)
                        {
                            minFineDiff = diff;
                            bestFineOffset = i;
                        }
                    }

                    // --- 应用结果 ---
                    if (bestFineOffset != -1)
                    {
                        long matchPosStart = searchRegionBegin + bestFineOffset;
                        long matchPosEnd = matchPosStart + templateLen;

                        long finalResultStart = currentStart;
                        long finalResultEnd = currentEnd;

                        if (adjustStart)
                        {
                            // 逆向：找到 Pre-End 的孪生兄弟，它的终点就是 Start 应该在的地方
                            finalResultStart = matchPosEnd;
                        }
                        else
                        {
                             // 正向：找到 Post-Start 的孪生兄弟，它的起点就是 End 应该在的地方
                             finalResultEnd = matchPosStart;
                        }

                        // 通过回调返回结果
                        onResult?.Invoke(finalResultStart, finalResultEnd);
                        
                        string modeStr = adjustStart ? "Reverse" : "Forward";
                        OnStatusChanged?.Invoke($"Smart Match {modeStr}: Diff={minFineDiff:F4}, New range: {finalResultStart}-{finalResultEnd}");
                    }
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Smart Match Error: {ex.Message}");
                }
                finally
                {
                    tempStream?.Dispose(); // 务必销毁临时流
                }
            });
        }

        /// <summary>
        /// 简单的降采样助手：每 N 个点取平均
        /// </summary>
        private float[] Downsample(float[] input, int factor)
        {
            if (factor <= 1) return input;
            int newSize = input.Length / factor;
            float[] output = new float[newSize];
            for (int i = 0; i < newSize; i++)
            {
                float sum = 0;
                // 简单的均值池化
                for (int j = 0; j < factor; j++)
                {
                    sum += input[i * factor + j];
                }
                output[i] = sum / factor;
            }
            return output;
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

        private float[] ReadSamplesFromStream(WaveStream stream, long startSample, int count)
        {
            if (stream == null) return new float[0];

            int bytesPerSample = stream.WaveFormat.BlockAlign;
            int bitsPerSample = stream.WaveFormat.BitsPerSample;
            byte[] raw = new byte[count * bytesPerSample];

            // 这里的 seek 是安全的，因为它是独立的临时流
            stream.Position = startSample * bytesPerSample;
            int bytesRead = stream.Read(raw, 0, raw.Length);
            
            int samplesRead = bytesRead / bytesPerSample;
            float[] samples = new float[samplesRead];

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
