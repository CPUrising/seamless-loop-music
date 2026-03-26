using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;
using System.Threading;

namespace seamless_loop_music
{
    /// <summary>
    /// 鏃犵紳寰幆闊抽鎾斁鍣?(涓绘帶閮ㄥ垎)
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
        public bool IsSeeking => _isSeeking;

        // 鍖归厤闀垮害閰嶇疆 (绉?
        public double MatchWindowSize { get; set; } = 1.0; 
        public double MatchSearchRadius { get; set; } = 5.0; 


        // 鍏紑璇诲彇鎺ュ彛
        public long LoopStartSample => _loopStartSample;
        public long LoopEndSample => _loopEndSample;
        public int SampleRate => _audioStream?.WaveFormat.SampleRate ?? 44100;

        // 鐘舵€佸洖璋冧簨浠?
        public event Action<string> OnStatusChanged;
        public event Action<PlaybackState> OnPlayStateChanged; 
        public event Action<long, int> OnAudioLoaded;
        public event Action OnLoopCycleCompleted;
        public event Action<Exception> OnPlaybackError;

        private string _currentFilePath; 
        private string _partBFilePath;   
        private bool _isLoading = false; 


        /// <summary>
        /// 璁剧疆寰幆璧峰閲囨牱鏁?
        /// </summary>
        public void SetLoopStartSample(long sample)
        {
            if (sample < 0 || sample >= _totalSamples)
            {
                OnStatusChanged?.Invoke($"閲囨牱鏁拌秴鍑鸿寖鍥?鏈夋晥鑼冨洿:0 ~ {_totalSamples - 1}");
                return;
            }
            _loopStartSample = sample;
            if (_loopStream != null)
            {
                _loopStream.LoopStartPosition = sample * _audioStream.WaveFormat.BlockAlign;
                // 娉ㄦ剰锛氳繖閲屼笉寮哄埗 ClearBuffer锛屽洜涓鸿捣濮嬬偣鍙樺姩閫氬父涓嶅奖鍝嶅綋鍓嶆鍦ㄦ挱鏀剧殑鐗囨
                // 鍙湁褰撹捣濮嬬偣琚涓哄綋鍓嶇偣涔嬪悗锛堟瘮濡傚線鍓嶆尓浜嗭級鎵嶉渶瑕佽€冭檻锛屼絾涓轰簡绋冲畾鏆備笉 Clear
            }
            
            OnStatusChanged?.Invoke($"Loop Start set: {sample} ({sample / (double)_audioStream.WaveFormat.SampleRate:F2}s)");
        }

        /// <summary>
        /// 璁剧疆寰幆缁撴潫閲囨牱鏁?
        /// </summary>
        public void SetLoopEndSample(long sample)
        {
            if (sample < 0)
            {
                sample = 0; // 0 琛ㄧず鏈熬
            }
            // 濡傛灉闈?涓斿皬浜庤捣鐐癸紝鎻愮ず閿欒锛堢畝鍗曟牎楠岋紝鍏蜂綋閫昏緫璁︰I灞傛垨LoopStream澶勭悊锛?
            if (sample > 0 && sample <= _loopStartSample)
            {
                OnStatusChanged?.Invoke($"閿欒: 寰幆缁堢偣蹇呴』澶т簬璧风偣({_loopStartSample})");
                return;
            }
            
            _loopEndSample = sample;
            if (_loopStream != null)
            {
                 long endPos = sample * _audioStream.WaveFormat.BlockAlign;
                 _loopStream.LoopEndPosition = (sample <= 0 || endPos > _audioStream.Length) ? _audioStream.Length : endPos;
                 
                 // 閲嶇偣鏀硅繘锛氬鏋滄柊鐨勫惊鐜粓鐐瑰湪褰撳墠鎾斁鐐逛箣鍓嶏紝蹇呴』绔嬪嵆娓呴櫎缂撳啿鍖哄苟 Seek
                 // 鍚﹀垯鐢ㄦ埛浼氬惉鍒板凡缁忓～鍏ョ紦鍐插尯鐨勩€佹湰璇ヨ鍒囨帀鐨勯煶棰?
                 if (sample > 0 && CurrentTime.TotalSeconds * SampleRate > sample)
                 {
                     SeekToSample(_loopStartSample); 
                 }
            }
            string endLabel = sample == 0 ? "End of file" : sample.ToString();
            OnStatusChanged?.Invoke($"Loop End set: {endLabel}");
        }

        public void SetLoopPoints(long startSample, long endSample)
        {
            SetLoopStartSample(startSample);
            SetLoopEndSample(endSample);
        }

        /// <summary>
        /// 寮€濮嬫挱鏀?(鏀寔浠庢殏鍋滃缁х画)
        /// </summary>
        public void Play()
        {
            if (_audioStream == null)
            {
                OnStatusChanged?.Invoke("Playback failed: Audio not loaded!");
                return;
            }

            // 濡傛灉鏄殏鍋滅姸鎬侊紝鐩存帴鎭㈠鎾斁
            if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Paused)
            {
                // 寮哄埗鍚屾涓€娆￠厤缃紝闃叉鎰忓
                if (_loopStream != null)
                {
                     _loopStream.LoopStartPosition = _loopStartSample * _audioStream.WaveFormat.BlockAlign;
                     // 鍚屾 LoopEnd
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

            // 濡傛灉宸茬粡鍦ㄦ挱鏀撅紝涔熷悓姝ヤ竴娆￠厤缃? 
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
                // 鍏ㄦ柊寮€濮嬶細鍒涘缓寰幆娴?
                _loopStream = new LoopStream(_audioStream, _loopStartSample, _loopEndSample);
                _loopStream.OnLoopCompleted += () => OnLoopCycleCompleted?.Invoke();

                // --- 寮傛缂撳啿绯荤粺閰嶇疆 ---
                _bufferedProvider = new BufferedWaveProvider(_loopStream.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(5), // 澧炲姞鍒?5 绉掞紝闃叉姣绾х殑婧㈠嚭
                    DiscardOnBufferOverflow = true
                };

                // 鍒涘缓鎾斁鍣?
                _wavePlayer = new WaveOutEvent
                {
                    DesiredLatency = 200, 
                    NumberOfBuffers = 3  
                };
                _wavePlayer.Volume = _volume; // Synchronize volume to new player
                LogDebug($"WaveOutEvent created with volume {_volume}");

                _wavePlayer.Init(_bufferedProvider);
                _wavePlayer.PlaybackStopped += (s, e) =>
                {
                    if (_isLoading) return;
                    
                    // 鏍稿績绋冲仴鎬ф敼杩涳細鎹曡幏搴曞眰寮傚父
                    if (e.Exception != null)
                    {
                        OnPlaybackError?.Invoke(e.Exception);
                        OnStatusChanged?.Invoke($"[Critical Error]: {e.Exception.Message}");
                    }
                    
                    if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Stopped)
                    {
                        OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
                        OnStatusChanged?.Invoke("Playback stopped.");
                    }
                };

                // 鍚姩鍚庡彴濉厖浠诲姟
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
        /// 鏆傚仠鎾斁
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
        /// 鍋滄鎾斁
        /// </summary>
        public void Stop()
        {
            if (_wavePlayer == null || _wavePlayer.PlaybackState == PlaybackState.Stopped) return;

            _wavePlayer?.Stop();
            OnPlayStateChanged?.Invoke(PlaybackState.Stopped);
            OnStatusChanged?.Invoke("Stopped.");
        }

        private float _volume = 1.0f;
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(1.0f, value));
                if (_wavePlayer != null)
                {
                    _wavePlayer.Volume = _volume;
                }
            }
        }

        public PlaybackState PlaybackState => _wavePlayer?.PlaybackState ?? PlaybackState.Stopped;

        public TimeSpan CurrentTime
        {
            get
            {
                lock (_streamLock)
                {
                    if (_loopStream != null && _audioStream != null && _bufferedProvider != null)
                    {
                        long playedBytes = _totalBytesReadSinceSeek - _bufferedProvider.BufferedBytes;
                        if (playedBytes < 0) playedBytes = 0;
                        
                        long playedSamples = playedBytes / _audioStream.WaveFormat.BlockAlign;
                        long currentSample = _seekTargetSample + playedSamples;
                        
                        if (_loopEndSample > 0 && currentSample >= _loopEndSample)
                        {
                             long loopLength = _loopEndSample - _loopStartSample;
                             if (loopLength > 0)
                             {
                                 long overshoot = currentSample - _loopEndSample;
                                 currentSample = _loopStartSample + (overshoot % loopLength);
                             }
                        }
                        else if (_loopEndSample <= 0 && currentSample >= _totalSamples)
                        {
                             long loopLength = _totalSamples - _loopStartSample;
                             if (loopLength > 0)
                             {
                                 long overshoot = currentSample - _totalSamples;
                                 currentSample = _loopStartSample + (overshoot % loopLength);
                             }
                        }

                        return TimeSpan.FromSeconds((double)currentSample / _audioStream.WaveFormat.SampleRate);
                    }
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

        private DateTime _seekTime = DateTime.MinValue;
        private long _seekTargetSample = 0;
        private long _totalBytesReadSinceSeek = 0;
        private readonly object _streamLock = new();

        public void SeekToSample(long sample)
        {
            if (_loopStream != null && _audioStream != null && _bufferedProvider != null)
            {
                lock (_streamLock) // 鎶㈠崰閿侊紝纭繚姝ゆ椂鍚庡彴娌℃湁鍦?Read
                {
                    _isSeeking = true;
                    _bufferedProvider.ClearBuffer(); 
                    Interlocked.Exchange(ref _totalBytesReadSinceSeek, 0);

                    long position = sample * _audioStream.WaveFormat.BlockAlign;
                    if (position < 0) position = 0;
                    long totalLen = _loopStream.Length;
                    if (position > totalLen) position = totalLen;
                    
                    _loopStream.Position = position;

                    // 璁板綍 Seek 鐘舵€侊紝鐢ㄤ簬骞虫粦 UI 鏄剧ず
                    _seekTargetSample = sample;
                    _seekTime = DateTime.Now;

                    // 娉ㄦ剰锛氳繖閲屼笉鍐嶇珛鍗冲皢 _isSeeking 璁句负 false
                    // 鎴戜滑璁╁悗鍙板～鍏呯嚎绋嬪湪濉叆绗竴鎵规暟鎹悗鍐嶅皢鍏堕噴鏀?
                }
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

            // 鏍稿績淇锛氬交搴曢噸缃繘搴﹁拷韪浉鍏崇殑鍐呴儴鐘舵€侊紝纭繚鏂版瓕鍔犺浇鍚庤绠楀噯纭?
            _seekTargetSample = 0;
            _isSeeking = false;
            Interlocked.Exchange(ref _totalBytesReadSinceSeek, 0);
        }

        public void Dispose()
        {
            Stop();
            DisposeAudioResources();
        }
        private void LogDebug(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio_debug.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
            OnStatusChanged?.Invoke(message);
        }
    }
}

