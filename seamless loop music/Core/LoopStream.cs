using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.IO;

namespace seamless_loop_music
{
    /// <summary>
    /// 实现无缝循环的音频流提供者
    /// </summary>
    public class LoopStream : IWaveProvider
    {
        private WaveStream _sourceStream;
        // 改为公有属性，允许动态修改
        public long LoopStartPosition { get; set; } 
        public long LoopEndPosition { get; set; }  
        private int _bytesPerSample;
        public bool EnableLooping { get; set; } = true;

        private readonly object _lockObject = new object(); // 线程锁

        public WaveFormat WaveFormat { get; private set; }

        /// <summary>
        /// 获取流的总长度 (字节)
        /// </summary>
        public long Length => _sourceStream.Length;

        /// <summary>
        /// 获取或设置当前位置 (字节)
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
        /// <param name="loopStartSample">循环起始采样点</param>
        /// <param name="loopEndSample">循环结束采样点(0表示文件末尾)</param>
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
            // 第一次播放从头开始，循环时才回到 LoopStart
            _sourceStream.Position = 0;
        }

        public event Action OnLoopCompleted;

        /// <summary>
        /// 读取音频数据(实现无缝循环的核心)
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_lockObject)
            {
                // 确保读取的字节数是块大小的整数倍
                count -= (count % _bytesPerSample);
                if (count <= 0) return 0;

                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    long currentPos = _sourceStream.Position;
                    long effectiveEnd = EnableLooping ? LoopEndPosition : _sourceStream.Length;
                    
                    // 核心逻辑：如果开启了循环且到达或超过循环点，立刻回
                    if (EnableLooping && currentPos >= effectiveEnd)
                    {
                        SafeSetPosition(LoopStartPosition);
                        OnLoopCompleted?.Invoke();
                        currentPos = _sourceStream.Position;
                    }

                    long remainingBytes = effectiveEnd - currentPos;
                    if (remainingBytes <= 0) break; // 到达有效终点（循环点或文件末端）

                    int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingBytes);
                    // 再次确保 bytesToRead 也是块对齐的
                    bytesToRead -= (bytesToRead % _bytesPerSample);
                    
                    if (bytesToRead <= 0)
                    {
                        if (EnableLooping)
                        {
                            // 剩下的太小了，不够一个块，强制触发跳转
                            SafeSetPosition(LoopStartPosition);
                            OnLoopCompleted?.Invoke();
                            continue;
                        }
                        else
                        {
                            // 到达文件末尾但不足一帧，直接结束
                            break;
                        }
                    }

                    int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                    if (bytesRead == 0)
                    {
                        if (EnableLooping)
                        {
                            // 源流意外结束，尝试跳转
                            SafeSetPosition(LoopStartPosition);
                            OnLoopCompleted?.Invoke();
                            if (_sourceStream.Position == currentPos) break; // 真的说不清了
                        }
                        else
                        {
                            break; // 真正结束
                        }
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
        /// 安全设置位置 (保证 BlockAlign 对齐)
        /// </summary>
        private void SafeSetPosition(long targetPos)
        {
            // 保证对齐到采样块
            targetPos -= (targetPos % _bytesPerSample);
            targetPos = Math.Max(0, Math.Min(targetPos, _sourceStream.Length));
            _sourceStream.Position = targetPos;
        }
    }
}
