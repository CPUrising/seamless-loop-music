using System;
using System.IO;
using System.Timers;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 温和防抖文件夹变动监视器
    /// 使用 FileSystemWatcher 监听，并带有 2 秒的防抖倒计时，防止密集写入时卡顿。
    /// </summary>
    public class GentleFolderWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher = new FileSystemWatcher();
        private readonly Timer _debounceTimer = new Timer();
        
        public event EventHandler FolderChanged;

        public GentleFolderWatcher(string folderPath, int intervalMs = 2000)
        {
            _debounceTimer.Interval = intervalMs;
            _debounceTimer.Elapsed += DebounceTimer_Elapsed;

            _watcher.Path = folderPath;
            _watcher.IncludeSubdirectories = true;

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void DebounceTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _debounceTimer.Stop();
            FolderChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Start() => _watcher.EnableRaisingEvents = true;
        
        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
            _debounceTimer.Stop();
        }

        public void Dispose()
        {
            Stop();
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }
    }
}
