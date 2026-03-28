using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    public partial class AudioLooper
    {
        /// <summary>
        /// 加载单音频文件
        /// </summary>
        public void LoadAudio(string filePath)
        {
            LoadAudio(filePath, null);
        }

        /// <summary>
        /// 加载音频。如果提供了 secondFilePath，则执行"物理预合并"方式(A+B)
        /// </summary>
        public void LoadAudio(string firstFilePath, string secondFilePath)
        {
            try
            {
                _isLoading = true;
                Stop();
                DisposeAudioResources();

                _currentFilePath = firstFilePath;
                _partBFilePath = secondFilePath;

                if (string.IsNullOrEmpty(secondFilePath))
                {
                    // 普通单文件加载模式
                    _audioStream = CreateAudioStream(firstFilePath);
                    if (_audioStream == null)
                    {
                        OnStatusChanged?.Invoke("Unsupported audio format!");
                        return;
                    }
                    _totalSamples = _audioStream.Length / _audioStream.WaveFormat.BlockAlign;
                    _loopStartSample = 0;
                    _loopEndSample = _totalSamples;
                }
                else
                {
                    // 强制方案：物理预合并 A + B
                    OnStatusChanged?.Invoke("Merging A/B parts in memory for perfect seamlessness...");
                    
                    using (var readerA = CreateAudioStream(firstFilePath))
                    using (var readerB = CreateAudioStream(secondFilePath))
                    {
                        if (readerA == null || readerB == null)
                        {
                            OnStatusChanged?.Invoke("Failed to load one of the A/B parts.");
                            return;
                        }

                        if (readerA.WaveFormat.SampleRate != readerB.WaveFormat.SampleRate || 
                            readerA.WaveFormat.Channels != readerB.WaveFormat.Channels)
                        {
                            OnStatusChanged?.Invoke("A/B parts format mismatch! Falling back to Part A.");
                            LoadAudio(firstFilePath);
                            return;
                        }

                        // 创建内存流并拼接
                        var memStream = new MemoryStream();
                        readerA.CopyTo(memStream);
                        long lengthA = memStream.Position; // 记录切分点
                        readerB.CopyTo(memStream);
                        memStream.Position = 0;

                        // 封装为原始音频流。这对于系统来说，这就是一段完整的、物理上连续的长音频。
                        _audioStream = new RawSourceWaveStream(memStream, readerA.WaveFormat);
                        
                        _totalSamples = _audioStream.Length / _audioStream.WaveFormat.BlockAlign;
                        _loopStartSample = lengthA / _audioStream.WaveFormat.BlockAlign; // 自动校准 B 开始
                        _loopEndSample = _totalSamples;
                    }
                }

                var waveFormat = _audioStream.WaveFormat;
                OnAudioLoaded?.Invoke(_totalSamples, waveFormat.SampleRate);
                OnStatusChanged?.Invoke(string.IsNullOrEmpty(secondFilePath) 
                    ? "Audio loaded. Set loop points and play!"
                    : "A/B Seamless Fusion Complete. Enjoy the perfect transition!");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Load operation failed: {ex.Message}");
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
    }
}
