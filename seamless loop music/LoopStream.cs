using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    /// <summary>
    /// 实现无缝循环的音频流提供器
    /// </summary>
    public class LoopStream : IWaveProvider
    {
        private WaveStream _sourceStream;
        private long _loopStartPosition;  // 循环起始位置(字节)
        private long _loopEndPosition;    // 循环结束位置(字节)
        private int _bytesPerSample;

        private readonly object _lockObject = new object(); // 线程锁

        public WaveFormat WaveFormat { get; private set; }

        /// <summary>
        /// 获取流的总长度(字节)
        /// </summary>
        public long Length => _sourceStream.Length;

        /// <summary>
        /// 获取或设置当前播放位置(字节)
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
                    // 确保设置的位置有效
                    value = Math.Max(0, Math.Min(value, _sourceStream.Length));
                    _sourceStream.Position = value;
                }
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="sourceStream">源音频流</param>
        /// <param name="loopStartSample">循环起始采样数</param>
        /// <param name="loopEndSample">循环结束采样数(0表示文件末尾)</param>
        public LoopStream(WaveStream sourceStream, long loopStartSample, long loopEndSample = 0)
        {
            _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
            WaveFormat = sourceStream.WaveFormat;
            _bytesPerSample = WaveFormat.BlockAlign;
            _loopStartPosition = loopStartSample * _bytesPerSample;

            long endPos = loopEndSample * _bytesPerSample;
            // 如果 loopEndSample 为 0 或者甚至比 sourceStream 还长，或者比 start 还小，则默认设为文件末尾
            if (loopEndSample <= 0 || endPos > sourceStream.Length || endPos <= _loopStartPosition)
            {
                _loopEndPosition = sourceStream.Length;
            }
            else
            {
                _loopEndPosition = endPos;
            }

            // 确保循环点在有效范围内
            if (_loopStartPosition < 0 || _loopStartPosition >= _sourceStream.Length)
            {
                _loopStartPosition = 0;
            }

            // 将流位置设置到循环起始点 (可选: 也可以从头开始播，到了 LoopEnd 再跳回 LoopStart。这里先保持之前的行为：Position = LoopStart 意味着从 LoopStart 开始播？)
            // 等等，通常 LoopStream 应该是从 0 开始播，播到 LoopEnd 跳回 LoopStart。
            // 之前的代码是 _sourceStream.Position = _loopStartPosition; 这意味着一播放就直接从 Loop点开始了。
            // 既然我们要支持任意区间循环，用户可能希望从头听到尾，或者从 Start 听到 End。
            // 保持原逻辑：Position 由外部控制，构造时默认指向 Start (因为以前就是这样)。
             _sourceStream.Position = _loopStartPosition;
        }

        /// <summary>
        /// 读取音频数据(实现无缝循环的核心)
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_lockObject) // 加锁防止冲突
            {
                // 确保读取的字节数是采样大小的整数倍
                if (count % _bytesPerSample != 0)
                {
                    count = (count / _bytesPerSample) * _bytesPerSample;
                }

                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    // 计算剩余可读字节数 (相对于 LoopEnd)
                    long currentPos = _sourceStream.Position;
                    if (currentPos >= _loopEndPosition)
                    {
                        // 已经超过结束点了，立即跳回
                         _sourceStream.Position = _loopStartPosition;
                         currentPos = _loopStartPosition;
                    }

                    long remainingBytes = _loopEndPosition - currentPos;
                    int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingBytes);

                    if (bytesToRead > 0)
                    {
                        // 读取数据
                        int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                        
                        // 就算 Read 说读到了，也要看实际 Position 是否超过了 end (虽然上面限制了 bytesToRead，但还是校验一下)
                        if (bytesRead == 0) 
                        {
                            // 读不到数据了(可能文件真的完了)，跳回 LoopStart
                            _sourceStream.Position = _loopStartPosition;
                        }
                        else 
                        {
                             totalBytesRead += bytesRead;
                        }
                    }
                    else
                    {
                        // 已到 LoopEnd，跳回 LoopStart
                        _sourceStream.Position = _loopStartPosition;
                    }
                }

                return totalBytesRead;
            }
        }
    }
}
