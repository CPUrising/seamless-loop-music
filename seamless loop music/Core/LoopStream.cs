using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    /// <summary>
    /// 瀹炵幇鏃犵紳寰幆鐨勯煶棰戞祦鎻愪緵鍣?
    /// </summary>
    public class LoopStream : IWaveProvider
    {
        private WaveStream _sourceStream;
        // 鏀逛负鍏湁灞炴€э紝鍏佽鍔ㄦ€佷慨鏀?
        public long LoopStartPosition { get; set; } 
        public long LoopEndPosition { get; set; }  
        private int _bytesPerSample;

        private readonly object _lockObject = new object(); // 绾跨▼閿?

        public WaveFormat WaveFormat { get; private set; }

        /// <summary>
        /// 鑾峰彇娴佺殑鎬婚暱搴?瀛楄妭)
        /// </summary>
        public long Length => _sourceStream.Length;

        /// <summary>
        /// 鑾峰彇鎴栬缃綋鍓嶆挱鏀句綅缃?瀛楄妭)
        /// </summary>
        public long Position
        {
            get
            {
                lock (_lockObject)
                {
                    return _sourceStream.Position;
                }
            }
            set
            {
                lock (_lockObject)
                {
                    SafeSetPosition(value);
                }
            }
        }

        /// <summary>
        /// 鏋勯€犲嚱鏁?
        /// </summary>
        /// <param name="sourceStream">婧愰煶棰戞祦</param>
        /// <param name="loopStartSample">寰幆璧峰閲囨牱鏁?/param>
        /// <param name="loopEndSample">寰幆缁撴潫閲囨牱鏁?0琛ㄧず鏂囦欢鏈熬)</param>
        public LoopStream(WaveStream sourceStream, long loopStartSample, long loopEndSample = 0)
        {
            _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
            WaveFormat = sourceStream.WaveFormat;
            _bytesPerSample = WaveFormat.BlockAlign;
            LoopStartPosition = loopStartSample * _bytesPerSample;

            long endPos = loopEndSample * _bytesPerSample;
            // 鏍￠獙 End
            if (loopEndSample <= 0 || endPos > sourceStream.Length || endPos <= LoopStartPosition)
            {
                LoopEndPosition = sourceStream.Length;
            }
            else
            {
                LoopEndPosition = endPos;
            }

            // 鏍￠獙 Start
            if (LoopStartPosition < 0 || LoopStartPosition >= _sourceStream.Length)
            {
                LoopStartPosition = 0;
            }

            // 鏋勯€犳椂: 榛樿瀹氫綅鍒?0 (鏂囦欢寮€澶?锛屽疄鐜?Intro + Loop 鐨勬晥鏋?
            // 棣栨鎾斁浠庡ご寮€濮嬶紝寰幆鏃舵墠璺冲洖 LoopStart
            _sourceStream.Position = 0;
        }

        public event Action OnLoopCompleted;

        /// <summary>
        /// 璇诲彇闊抽鏁版嵁(瀹炵幇鏃犵紳寰幆鐨勬牳蹇?
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_lockObject)
            {
                // 纭繚璇诲彇鐨勫瓧鑺傛暟鏄潡澶у皬鐨勬暣鏁板€?
                count -= (count % _bytesPerSample);
                if (count <= 0) return 0;

                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    long currentPos = _sourceStream.Position;
                    
                    // 鏍稿績閫昏緫锛氬鏋滃埌杈炬垨瓒呰繃寰幆鐐癸紝绔嬪嵆璺冲洖
                    if (currentPos >= LoopEndPosition)
                    {
                        SafeSetPosition(LoopStartPosition);
                        OnLoopCompleted?.Invoke();
                        currentPos = _sourceStream.Position;
                    }

                    long remainingBytes = LoopEndPosition - currentPos;
                    if (remainingBytes <= 0) break; // 闃叉姝诲惊鐜?

                    int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingBytes);
                    // 鍐嶆纭繚 bytesToRead 涔熸槸鍧楀榻愮殑
                    bytesToRead -= (bytesToRead % _bytesPerSample);
                    
                    if (bytesToRead <= 0)
                    {
                        // 鍓╀笅鐨勫お灏忎簡锛屼笉澶熶竴涓潡锛屽己鍒惰Е鍙戣烦杞?
                        SafeSetPosition(LoopStartPosition);
                        OnLoopCompleted?.Invoke();
                        continue;
                    }

                    int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                    if (bytesRead == 0)
                    {
                        // 婧愭祦鎰忓缁撴潫锛屽皾璇曡烦鍥?
                        SafeSetPosition(LoopStartPosition);
                        OnLoopCompleted?.Invoke();
                        if (_sourceStream.Position == currentPos) break; // 鐪熺殑璇讳笉鍔ㄤ簡
                    }
                    else
                    {
                        totalBytesRead += bytesRead;
                    }
                }

                return totalBytesRead;
            }
        }

        /// <summary>
        /// 瀹夊叏璁剧疆浣嶇疆 (纭繚 BlockAlign 瀵归綈)
        /// </summary>
        private void SafeSetPosition(long targetPos)
        {
            // 纭繚瀵归綈鍒伴噰鏍峰潡
            targetPos -= (targetPos % _bytesPerSample);
            targetPos = Math.Max(0, Math.Min(targetPos, _sourceStream.Length));
            _sourceStream.Position = targetPos;
        }
    }
}

