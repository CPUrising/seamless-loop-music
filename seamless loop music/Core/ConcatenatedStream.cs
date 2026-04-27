using NAudio.Wave;
using System;

namespace seamless_loop_music
{
    /// <summary>
    /// 逻辑拼接流：按顺序连接两个 WaveStream，实现零内存损耗的无缝播放
    /// </summary>
    public class ConcatenatedStream : WaveStream
    {
        private readonly WaveStream _streamA;
        private readonly WaveStream _streamB;
        private readonly long _lengthA;
        private readonly long _totalLength;
        private readonly object _lockObject = new object();

        public override WaveFormat WaveFormat => _streamA.WaveFormat;

        public override long Length => _totalLength;

        public override long Position
        {
            get
            {
                lock (_lockObject)
                {
                    if (_streamA.Position < _lengthA)
                    {
                        return _streamA.Position;
                    }
                    return _lengthA + _streamB.Position;
                }
            }
            set
            {
                lock (_lockObject)
                {
                    if (value < _lengthA)
                    {
                        _streamA.Position = value;
                        _streamB.Position = 0;
                    }
                    else
                    {
                        _streamA.Position = _lengthA;
                        _streamB.Position = Math.Min(value - _lengthA, _streamB.Length);
                    }
                }
            }
        }

        public ConcatenatedStream(WaveStream streamA, WaveStream streamB)
        {
            _streamA = streamA ?? throw new ArgumentNullException(nameof(streamA));
            _streamB = streamB ?? throw new ArgumentNullException(nameof(streamB));

            if (streamA.WaveFormat.SampleRate != streamB.WaveFormat.SampleRate ||
                streamA.WaveFormat.Channels != streamB.WaveFormat.Channels ||
                streamA.WaveFormat.BitsPerSample != streamB.WaveFormat.BitsPerSample)
            {
                throw new ArgumentException("Part A and Part B must have the same WaveFormat.");
            }

            _lengthA = streamA.Length;
            _totalLength = _lengthA + streamB.Length;
            
            _streamA.Position = 0;
            _streamB.Position = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lockObject)
            {
                int totalRead = 0;

                // 如果还在第一段
                if (_streamA.Position < _lengthA)
                {
                    int toReadA = (int)Math.Min(count, _lengthA - _streamA.Position);
                    int readA = _streamA.Read(buffer, offset, toReadA);
                    totalRead += readA;
                    offset += readA;
                    count -= readA;
                }

                // 如果第一段读完了，且还有剩余空间，则读第二段
                if (count > 0 && _streamA.Position >= _lengthA)
                {
                    int readB = _streamB.Read(buffer, offset, count);
                    totalRead += readB;
                }

                return totalRead;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _streamA.Dispose();
                _streamB.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
