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
        // 改为公有属性，允许动态修改
        public long LoopStartPosition { get; set; } 
        public long LoopEndPosition { get; set; }  
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
            LoopStartPosition = loopStartSample * _bytesPerSample;

            long endPos = loopEndSample * _bytesPerSample;
            // 校验 End
            if (loopEndSample <= 0 || endPos > sourceStream.Length || endPos <= LoopStartPosition)
            {
                LoopEndPosition = sourceStream.Length;
            }
            else
            {
                LoopEndPosition = endPos;
            }

            // 校验 Start
            if (LoopStartPosition < 0 || LoopStartPosition >= _sourceStream.Length)
            {
                LoopStartPosition = 0;
            }

            // 构造时: 默认定位到 Start 处开始播放? 
            // 如果为了严谨，我们应该遵循: 如果是重新创建流，可能用户期望从 Start 开始。
            // 但如果是切歌后的首次播放，我们可能希望从头播。
            // 构造时: 默认定位到 0 (文件开头)，实现 Intro + Loop 的效果
            // 首次播放从头开始，循环时才跳回 LoopStart
            _sourceStream.Position = 0;
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
                    if (currentPos >= LoopEndPosition)
                    {
                        // 已经超过结束点了，立即跳回
                         _sourceStream.Position = LoopStartPosition;
                         currentPos = LoopStartPosition;
                    }

                    long remainingBytes = LoopEndPosition - currentPos;
                    int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingBytes);

                    if (bytesToRead > 0)
                    {
                        // 读取数据
                        int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                        
                        if (bytesRead == 0) 
                        {
                            // 读不到数据了，跳回 LoopStart
                            _sourceStream.Position = LoopStartPosition;
                        }
                        else 
                        {
                             totalBytesRead += bytesRead;
                        }
                    }
                    else
                    {
                        // 已到 LoopEnd，跳回 LoopStart
                        _sourceStream.Position = LoopStartPosition;
                    }
                }

                return totalBytesRead;
            }
        }
    }
}
