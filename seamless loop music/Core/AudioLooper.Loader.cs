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
                    _abSeamSample = -1; 
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
                    // 逻辑拼接方案：使用 ConcatenatedStream 实现 A + B
                    OnStatusChanged?.Invoke("Fusing A/B parts logically...");
                    
                    var readerA = CreateAudioStream(firstFilePath);
                    var readerB = CreateAudioStream(secondFilePath);

                    if (readerA == null || readerB == null)
                    {
                        readerA?.Dispose();
                        readerB?.Dispose();
                        OnStatusChanged?.Invoke("Failed to load one of the A/B parts.");
                        return;
                    }

                    if (readerA.WaveFormat.SampleRate != readerB.WaveFormat.SampleRate || 
                        readerA.WaveFormat.Channels != readerB.WaveFormat.Channels)
                    {
                        OnStatusChanged?.Invoke("A/B parts format mismatch! Falling back to Part A.");
                        readerA.Dispose();
                        readerB.Dispose();
                        LoadAudio(firstFilePath);
                        return;
                    }

                    // 记录分界点 (Part A 的结束位置)
                    _abSeamSample = readerA.Length / readerA.WaveFormat.BlockAlign;

                    // 使用自定义的拼接流
                    _audioStream = new ConcatenatedStream(readerA, readerB);
                    
                    _totalSamples = _audioStream.Length / _audioStream.WaveFormat.BlockAlign;
                    _loopStartSample = _abSeamSample; // 默认循环 Part B
                    _loopEndSample = _totalSamples;
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
