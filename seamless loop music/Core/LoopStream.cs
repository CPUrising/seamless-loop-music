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
                    SafeSetPosition(value);
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

            // 构造时: 默认定位到 0 (文件开头)，实现 Intro + Loop 的效果
            // 首次播放从头开始，循环时才跳回 LoopStart
            _sourceStream.Position = 0;
        }

        public event Action OnLoopCompleted;

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
                        SafeSetPosition(LoopStartPosition);
                        currentPos = _sourceStream.Position; // 更新 currentPos!
                        OnLoopCompleted?.Invoke(); 
                    }

                    long remainingBytes = LoopEndPosition - currentPos;
                    // 如果 remainingBytes <= 0，说明还在 LoopEnd 之后（虽然刚seek过），可能 Seek 到了奇怪的位置或者 LoopStart >= LoopEnd
                    if (remainingBytes <= 0) 
                    {
                         // 强制归零以防死锁，或结束
                         remainingBytes = 0; 
                    }

                    int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingBytes);

                    if (bytesToRead > 0)
                    {
                        // 读取数据
                        int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                        
                        if (bytesRead == 0) 
                        {
                            // 读不到数据了，跳回 LoopStart
                            SafeSetPosition(LoopStartPosition);
                            OnLoopCompleted?.Invoke(); 
                            // 此时 currentPos 变了，继续循环
                        }
                        else 
                        {
                             totalBytesRead += bytesRead;
                        }
                    }
                    else
                    {
                        // 已到 LoopEnd，跳回 LoopStart
                        SafeSetPosition(LoopStartPosition);
                        OnLoopCompleted?.Invoke(); 
                    }
                }

                return totalBytesRead;
            }
        }

        /// <summary>
        /// 安全设置位置
        /// </summary>
        private void SafeSetPosition(long targetPos)
        {
            // 确保位置在合法范围内
            targetPos = Math.Max(0, Math.Min(targetPos, _sourceStream.Length));
            _sourceStream.Position = targetPos;
        }
    }
}
