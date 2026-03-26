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
        /// 鏅鸿兘瀵绘壘鏈€浣冲惊鐜偣 (寮傛鐗?- 鍩轰簬閲戝瓧濉旀悳绱?Pyramid Search)
        /// 浣跨敤鐙珛鏂囦欢娴侊紝閬垮厤涓庢挱鏀剧嚎绋嬪啿绐併€?
        /// </summary>
        /// <param name="adjustStart">True: 鍥哄畾 End 鎵?Start (閫嗗悜); False: 鍥哄畾 Start 鎵?End (姝ｅ悜)</param>
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
                    // 1. 鍒涘缓鐙珛鐨勪复鏃舵祦
                    if (string.IsNullOrEmpty(pathB))
                    {
                        tempStream = CreateAudioStream(pathA);
                    }
                    else
                    {
                        // A/B 妯″紡锛氶噸鏂板姞杞?A 鍜?B 鍒嗘暎鍒嗘瀽浠诲姟鍘嬪姏
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

                    // --- 鍙傛暟鍑嗗 ---
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

                    // 浠庝复鏃舵祦璇诲彇鏁版嵁 (鎵归噺璇诲彇)
                    float[] templateFull = ReadSamplesFromStream(tempStream, templateStartPos, (int)templateLen);
                    float[] searchBufferFull = ReadSamplesFromStream(tempStream, searchRegionBegin, (int)searchLen);

                    if (templateFull.Length < 100 || searchBufferFull.Length < templateFull.Length)
                    {
                         OnStatusChanged?.Invoke("Match Error: Data insufficient.");
                         onResult?.Invoke(currentStart, currentEnd);
                         return;
                    }

                    // --- 绗竴灞傦細绮楁悳 ---
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

                    // --- 绗簩灞傦細绮炬悳 ---
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
        /// 绠€鍗曠殑闄嶉噰鏍峰姪鎵嬶細姣?N 涓偣鍙栧钩鍧?
        /// </summary>
        private float[] Downsample(float[] input, int factor)
        {
            if (factor <= 1) return input;
            int newSize = input.Length / factor;
            float[] output = new float[newSize];
            for (int i = 0; i < newSize; i++)
            {
                float sum = 0;
                // 绠€鍗曠殑鍧囧€兼睜鍖?
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

            // 淇濆瓨褰撳墠娴佷綅缃敤浜庢仮澶?
            long oldPos = _audioStream.Position;
            
            _audioStream.Position = startSample * bytesPerSample;
            int bytesRead = _audioStream.Read(raw, 0, raw.Length);
            
            _audioStream.Position = oldPos; // 鎭㈠浣嶇疆

            int samplesRead = bytesRead / bytesPerSample;
            float[] samples = new float[samplesRead];

            // 绠€鍗曠殑 PCM 杞?Float 瑙ｆ瀽
            // 娉ㄦ剰锛氳繖閲屽彧鍙栫涓€涓０閬撳仛鍖归厤鍗冲彲锛屼负浜嗛€熷害銆傚鏋滄槸绔嬩綋澹帮紝鍙栧乏澹伴亾銆?
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
                // 鏆備笉澶勭悊8bit鎴?4bit锛岄€氬父澶熺敤
            }

            return samples;
        }

        private float[] ReadSamplesFromStream(WaveStream stream, long startSample, int count)
        {
            if (stream == null) return new float[0];

            int bytesPerSample = stream.WaveFormat.BlockAlign;
            int bitsPerSample = stream.WaveFormat.BitsPerSample;
            byte[] raw = new byte[count * bytesPerSample];

            // 杩欓噷鐨?seek 鏄畨鍏ㄧ殑锛屽洜涓哄畠鏄嫭绔嬬殑涓存椂娴?
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

