using NAudio.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace seamless_loop_music
{
    public partial class AudioLooper
    {
        private void StartFillerTask()
        {
            StopFillerTask();
            _fillerCts = new CancellationTokenSource();
            var token = _fillerCts.Token;
            
            Task.Run(() => BackgroundFillLoop(token), token);
        }

        private void StopFillerTask()
        {
            _fillerCts?.Cancel();
            _fillerCts = null;
        }

        private async Task BackgroundFillLoop(CancellationToken token)
        {
            byte[] readBuffer = new byte[16384]; // 16KB 每次读取量
            
            while (!token.IsCancellationRequested)
            {
                if (_isSeeking) // 等待 Seek 完成
                {
                    await Task.Delay(10, token);
                    continue;
                }

                if (_bufferedProvider == null || _loopStream == null) break;

                // 保持缓冲区在 400ms 左右的余量，既能抗抖动，又不至于让事件太超前
                TimeSpan buffered = _bufferedProvider.BufferedDuration;
                if (buffered.TotalMilliseconds < 400)
                {
                    try 
                    {
                        int read = _loopStream.Read(readBuffer, 0, readBuffer.Length);
                        if (read > 0)
                        {
                            _bufferedProvider.AddSamples(readBuffer, 0, read);
                        }
                        else 
                        {
                            await Task.Delay(20, token);
                        }
                    }
                    catch { break; }
                }
                else 
                {
                    // 粮草充足，休息一会儿
                    await Task.Delay(30, token);
                }
            }
        }
    }
}
