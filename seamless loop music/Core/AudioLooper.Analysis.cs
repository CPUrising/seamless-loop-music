using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;
using System.Threading.Tasks;

namespace seamless_loop_music
{
    public partial class AudioLooper
    {
        /// <summary>
        /// 智能寻找最佳循环点 (异步�?- 基于金字塔搜�?Pyramid Search)
        /// 使用独立文件流，避免与播放线程冲突�?
        /// </summary>
        /// <param name="adjustStart">True: 固定 End �?Start (逆向); False: 固定 Start �?End (正向)</param>
        public async void FindBestLoopPointsAsync(long currentStart, long currentEnd, bool adjustStart, Action<long, long> onResult)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                OnStatusChanged?.Invoke("Error: File path not available for analysis.");
                onResult?.Invoke(currentStart, currentEnd);
                return;
            }

            string pathA = _currentFilePath;
            string pathB = _partBFilePath;
            
            await Task.Run(() =>
            {
                WaveStream tempStream = null;
                try
                {
                    // 1. 创建独立的临时流
                    if (string.IsNullOrEmpty(pathB))
                    {
                        tempStream = CreateAudioStream(pathA);
                    }
                    else
                    {
                        // A/B 模式：重新加�?A �?B 分散分析任务压力
                        using (var rA = CreateAudioStream(pathA))
                        using (var rB = CreateAudioStream(pathB))
                        {
                            if (rA != null && rB != null)
                            {
                                var ms = new MemoryStream();
                                rA.CopyTo(ms);
                                rB.CopyTo(ms);
                                ms.Position = 0;
                                tempStream = new RawSourceWaveStream(ms, rA.WaveFormat);
                            }
                        }
                    }

                    if (tempStream == null)
                    {
                        onResult?.Invoke(currentStart, currentEnd);
                        return;
                    }

                    var fmt = tempStream.WaveFormat;
                    int sampleRate = fmt.SampleRate;
                    long totalSamples = tempStream.Length / fmt.BlockAlign;

                    // --- 参数准备 ---
                    int windowSize = (int)(sampleRate * MatchWindowSize); 
                    
                    if (adjustStart) {
                         if (currentEnd < windowSize) windowSize = (int)currentEnd;
                    } else {
                         if (currentStart + windowSize > totalSamples) windowSize = (int)(totalSamples - currentStart);
                    }
                    
                    if (windowSize < 1024) 
                    {
                         OnStatusChanged?.Invoke("Match Error: Sample region too small.");
                         onResult?.Invoke(currentStart, currentEnd);
                         return;
                    }

                    long searchRadius = (long)(sampleRate * MatchSearchRadius); 
                    long templateStartPos, templateEndPos;
                    long searchRegionCenter;

                    if (adjustStart)
                    {
                        templateEndPos = currentEnd;
                        templateStartPos = templateEndPos - windowSize;
                        searchRegionCenter = currentStart;
                    }
                    else
                    {
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
                    if (searchLen < templateLen) 
                    {
                         OnStatusChanged?.Invoke("Match Error: Search region smaller than template.");
                         onResult?.Invoke(currentStart, currentEnd);
                         return;
                    }

                    // 从临时流读取数据 (批量读取)
                    float[] templateFull = ReadSamplesFromStream(tempStream, templateStartPos, (int)templateLen);
                    float[] searchBufferFull = ReadSamplesFromStream(tempStream, searchRegionBegin, (int)searchLen);

                    if (templateFull.Length < 100 || searchBufferFull.Length < templateFull.Length)
                    {
                         OnStatusChanged?.Invoke("Match Error: Data insufficient.");
                         onResult?.Invoke(currentStart, currentEnd);
                         return;
                    }

                    // --- 第一层：粗搜 ---
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

                    if (bestCoarseOffset == -1)
                    {
                         OnStatusChanged?.Invoke("Match Error: No clear pattern found.");
                         onResult?.Invoke(currentStart, currentEnd);
                         return;
                    }

                    // --- 第二层：精搜 ---
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

                    if (bestFineOffset != -1)
                    {
                        long matchPosStart = searchRegionBegin + bestFineOffset;
                        long matchPosEnd = matchPosStart + templateLen;

                        long finalResultStart = currentStart;
                        long finalResultEnd = currentEnd;

                        if (adjustStart) finalResultStart = matchPosEnd;
                        else finalResultEnd = matchPosStart;

                        onResult?.Invoke(finalResultStart, finalResultEnd);
                        OnStatusChanged?.Invoke($"Match OK! Diff: {minFineDiff:F3}");
                    }
                    else
                    {
                        onResult?.Invoke(currentStart, currentEnd);
                    }
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Smart Match Error: {ex.Message}");
                    onResult?.Invoke(currentStart, currentEnd);
                }
                finally
                {
                    tempStream?.Dispose();
                }
            });
        }

        /// <summary>
        /// 简单的降采样助手：�?N 个点取平�?
        /// </summary>
        private float[] Downsample(float[] input, int factor)
        {
            if (factor <= 1) return input;
            int newSize = input.Length / factor;
            float[] output = new float[newSize];
            for (int i = 0; i < newSize; i++)
            {
                float sum = 0;
                // 简单的均值池�?
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

            // 保存当前流位置用于恢�?
            long oldPos = _audioStream.Position;
            
            _audioStream.Position = startSample * bytesPerSample;
            int bytesRead = _audioStream.Read(raw, 0, raw.Length);
            
            _audioStream.Position = oldPos; // 恢复位置

            int samplesRead = bytesRead / bytesPerSample;
            float[] samples = new float[samplesRead];

            // 简单的 PCM �?Float 解析
            // 注意：这里只取第一个声道做匹配即可，为了速度。如果是立体声，取左声道�?
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
                // 暂不处理8bit�?4bit，通常够用
            }

            return samples;
        }

        private float[] ReadSamplesFromStream(WaveStream stream, long startSample, int count)
        {
            if (stream == null) return new float[0];

            int bytesPerSample = stream.WaveFormat.BlockAlign;
            int bitsPerSample = stream.WaveFormat.BitsPerSample;
            byte[] raw = new byte[count * bytesPerSample];

            // 这里�?seek 是安全的，因为它是独立的临时�?
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
    }
}

