using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    public partial class AudioLooper
    {
        /// <summary>
        /// 鍔犺浇鍗曢煶棰戞枃浠?
        /// </summary>
        public void LoadAudio(string filePath)
        {
            LoadAudio(filePath, null);
        }

        /// <summary>
        /// 鍔犺浇闊抽銆傚鏋滄彁渚涗簡 secondFilePath锛屽垯鎵ц鈥滅墿鐞嗛鍚堝苟鈥濇柟妗?(A+B)
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
                    // 鏅€氬崟鏂囦欢鍔犺浇妯″紡
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
                    // 鏆村姏鏂规锛氱墿鐞嗛鍚堝苟 A + B
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

                        // 鍒涘缓鍐呭瓨娴佸苟鎷兼帴
                        var memStream = new MemoryStream();
                        readerA.CopyTo(memStream);
                        long lengthA = memStream.Position; // 璁板綍鎺ョ紳鐐?
                        readerB.CopyTo(memStream);
                        memStream.Position = 0;

                        // 鍖呰鎴愬師濮嬫尝褰㈡祦銆傝繖鏍峰浜庣郴缁熸潵璇达紝杩欏氨鏄竴棣栧畬鏁寸殑銆佺墿鐞嗕笂杩炵画鐨勯暱姝屻€?
                        _audioStream = new RawSourceWaveStream(memStream, readerA.WaveFormat);
                        
                        _totalSamples = _audioStream.Length / _audioStream.WaveFormat.BlockAlign;
                        _loopStartSample = lengthA / _audioStream.WaveFormat.BlockAlign; // 鑷姩鐬勫噯 B 寮€澶?
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
        /// 鍒涘缓瀵瑰簲鏍煎紡鐨勯煶棰戞祦
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

