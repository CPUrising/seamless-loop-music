using System;
using System.Collections.Concurrent;
using System.Threading;
using NAudio.Wave;

namespace seamless_loop_music
{
    /// <summary>
    /// 把底层音频流的创建与全部访问固定到同一个专用线程上。
    /// 原因：Media Foundation（AudioFileReader 处理 m4a/wma 时使用）的 COM 对象跨线程访问会抛
    /// InvalidCastException（IMFSourceReader E_NOINTERFACE），而播放器会在填充线程与 UI 线程分别访问同一音频流。
    /// </summary>
    public sealed class ThreadAffineWaveStream : WaveStream
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly Thread _pump;
        private WaveStream _inner;
        private Exception _startupError;
        private volatile bool _disposed;

        public ThreadAffineWaveStream(Func<WaveStream> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _pump = new Thread(() => PumpLoop(factory))
            {
                IsBackground = true,
                Name = "AudioStreamPump"
            };
            _pump.SetApartmentState(ApartmentState.MTA);
            _pump.Start();

            _ready.Wait();
            _ready.Dispose();

            if (_startupError != null)
            {
                _disposed = true;
                throw new InvalidOperationException("音频流初始化失败：" + _startupError.Message, _startupError);
            }

            WaveFormat = Invoke(() => _inner.WaveFormat);
            Length = Invoke(() => _inner.Length);
        }

        public override WaveFormat WaveFormat { get; }

        public override long Length { get; }

        public override long Position
        {
            get => Invoke(() => _inner.Position);
            set => Invoke(() =>
            {
                _inner.Position = value;
                return 0;
            });
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Invoke(() => _inner.Read(buffer, offset, count));
        }

        public override void Flush()
        {
            Invoke(() =>
            {
                _inner.Flush();
                return 0;
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    Execute(() =>
                    {
                        _inner?.Dispose();
                        _inner = null;
                    });
                }
                catch
                {
                    // 释放阶段的异常不影响调用方
                }

                try
                {
                    _queue.CompleteAdding();
                }
                catch
                {
                    // 队列可能已关闭
                }

                _pump.Join(1000);
            }

            base.Dispose(disposing);
        }

        private void PumpLoop(Func<WaveStream> factory)
        {
            try
            {
                _inner = factory();
            }
            catch (Exception ex)
            {
                _startupError = ex;
            }
            finally
            {
                _ready.Set();
            }

            if (_startupError != null) return;

            try
            {
                foreach (var work in _queue.GetConsumingEnumerable())
                {
                    work();
                }
            }
            catch
            {
                // 队列关闭；工作项自身的异常已在 Execute 内回传给调用方
            }
        }

        private T Invoke<T>(Func<T> work)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ThreadAffineWaveStream));

            T result = default(T);
            Execute(() => result = work());
            return result;
        }

        private void Execute(Action work)
        {
            Exception error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                try
                {
                    _queue.Add(() =>
                    {
                        try
                        {
                            work();
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                        }
                        finally
                        {
                            done.Set();
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                    throw new ObjectDisposedException(nameof(ThreadAffineWaveStream));
                }

                done.Wait();
            }

            if (error != null) throw error;
        }
    }
}
