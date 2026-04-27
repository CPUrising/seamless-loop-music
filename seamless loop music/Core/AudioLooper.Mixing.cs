using NAudio.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace seamless_loop_music
{
    public partial class AudioLooper
    {
        private Thread _fillerThread;

        private void StartFillerTask()
        {
            StopFillerTask();
            _fillerCts = new CancellationTokenSource();
            
            _fillerThread = new Thread(() => LoopFillerLoop(_fillerCts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "AudioLooperFiller"
            };
            _fillerThread.Start();
        }

        private void StopFillerTask()
        {
            _fillerCts?.Cancel();
            _fillerThread?.Join(200);
            _fillerCts = null;
            _fillerThread = null;
        }

        private void LoopFillerLoop(CancellationToken token)
        {
            byte[] readBuffer = new byte[32768];
            
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isSeeking) 
                    {
                        if (_loopStream != null && _bufferedProvider != null)
                        {
                            lock (_streamLock)
                            {
                                int firstRead = _loopStream.Read(readBuffer, 0, readBuffer.Length);
                                if (firstRead > 0)
                                {
                                    _totalBytesReadSinceSeek += firstRead;
                                    _bufferedProvider.AddSamples(readBuffer, 0, firstRead);
                                    _isSeeking = false;
                                }
                            }
                        }
                        
                        if (_isSeeking)
                        {
                            Thread.Sleep(10);
                            continue;
                        }
                    }

                    if (_bufferedProvider == null || _loopStream == null) break;

                    TimeSpan buffered = _bufferedProvider.BufferedDuration;
                    if (buffered.TotalMilliseconds < 2500)
                    {
                        try 
                        {
                            int read = 0;
                            lock (_streamLock)
                            {
                                if (_loopStream != null && !_isSeeking) 
                                {
                                    read = _loopStream.Read(readBuffer, 0, readBuffer.Length);
                                    if (read > 0)
                                    {
                                        _totalBytesReadSinceSeek += read;
                                        _bufferedProvider.AddSamples(readBuffer, 0, read);
                                    }
                                }
                            }
                            if (read <= 0) 
                            {
                                // 核心改动：如果不在循环模式且读取结束，标记自然结束
                                if (!_isSeamlessLoopEnabled && !_isSeeking)
                                {
                                    _isEndingNaturally = true;
                                    break; // 停止填充线程，等待播放器排空缓冲区后停止
                                }
                                Thread.Sleep(20);
                            }
                        }
                        catch { break; }
                    }
                    else 
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception) { }
        }
    }
}
