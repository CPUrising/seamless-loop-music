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
        private int _bytesPerSample;

        public WaveFormat WaveFormat { get; private set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="sourceStream">源音频流</param>
        /// <param name="loopStartSample">循环起始采样数</param>
        public LoopStream(WaveStream sourceStream, long loopStartSample)
        {
            _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
            WaveFormat = sourceStream.WaveFormat;
            _bytesPerSample = WaveFormat.BlockAlign;
            _loopStartPosition = loopStartSample * _bytesPerSample;

            // 确保循环点在有效范围内
            if (_loopStartPosition < 0 || _loopStartPosition >= sourceStream.Length)
            {
                _loopStartPosition = 0;
            }

            // 将流位置设置到循环起始点
            _sourceStream.Position = _loopStartPosition;
        }

        /// <summary>
        /// 读取音频数据(实现无缝循环的核心)
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            // 确保读取的字节数是采样大小的整数倍
            if (count % _bytesPerSample != 0)
            {
                count = (count / _bytesPerSample) * _bytesPerSample;
            }

            int totalBytesRead = 0;

            while (totalBytesRead < count)
            {
                // 计算剩余可读字节数
                long remainingBytes = _sourceStream.Length - _sourceStream.Position;
                int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingBytes);

                if (bytesToRead > 0)
                {
                    // 读取数据
                    int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                    totalBytesRead += bytesRead;

                    if (bytesRead == 0)
                    {
                        // 已到文件末尾,跳回循环点
                        _sourceStream.Position = _loopStartPosition;
                    }
                }
                else
                {
                    // 已到文件末尾,跳回循环点(无缝循环的关键!)
                    _sourceStream.Position = _loopStartPosition;
                }
            }

            return totalBytesRead;
        }
    }
}
