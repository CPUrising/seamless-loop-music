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
                Priority = ThreadPriority.AboveNormal, // VIP 通道！
                Name = "AudioLooperFiller"
            };
            _fillerThread.Start();
        }

        private void StopFillerTask()
        {
            _fillerCts?.Cancel();
            _fillerThread?.Join(200); // 给它一点时间优雅停止
            _fillerCts = null;
            _fillerThread = null;
        }

        private void LoopFillerLoop(CancellationToken token)
        {
            byte[] readBuffer = new byte[32768]; // 32KB 每次读取量，更加豪迈
            
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isSeeking) 
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (_bufferedProvider == null || _loopStream == null) break;

                    // 维持 3 秒存粮，即使 CPU 忙其他的，这些也够吃很久
                    TimeSpan buffered = _bufferedProvider.BufferedDuration;
                    if (buffered.TotalMilliseconds < 3000)
                    {
                        try 
                        {
                            int read = 0;
                            lock (_streamLock)
                            {
                                // 获取锁之后还要再检查一次，防止刚才有人要Seek
                                if (_loopStream != null && !_isSeeking) 
                                {
                                    read = _loopStream.Read(readBuffer, 0, readBuffer.Length);
                                }
                            }
                            
                            if (read > 0)
                            {
                                _bufferedProvider.AddSamples(readBuffer, 0, read);
                                Interlocked.Add(ref _totalBytesReadSinceSeek, read);
                            }
                            else 
                            {
                                // End of stream reached (LoopStream handles looping internally, so this might be error)
                                Thread.Sleep(20);
                            }
                        }
                        catch { break; }
                    }
                    else 
                    {
                        // 粮草极其充足，休息一会儿
                        Thread.Sleep(50);
                    }

                    // 无论是否休息，都检查一下进度通知
                    CheckAndNotifyPosition();
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception) { }
        }
    }
}
