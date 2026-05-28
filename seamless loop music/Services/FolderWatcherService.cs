using System;
using System.Collections.Generic;
using System.IO;
using seamless_loop_music.Data;

namespace seamless_loop_music.Services
{
    public interface IFolderWatcherService
    {
        void Initialize();
        void RefreshWatchers();
        void StopWatching();
    }

    public class FolderWatcherService : IFolderWatcherService
    {
        private readonly IDatabaseHelper _dbHelper;
        private readonly Lazy<IPlayerService> _playerService; // 使用 Lazy 以防依赖注入循环依赖
        private readonly List<GentleFolderWatcher> _watchers = new List<GentleFolderWatcher>();

        public FolderWatcherService(IDatabaseHelper dbHelper, Lazy<IPlayerService> playerService)
        {
            _dbHelper = dbHelper;
            _playerService = playerService;
        }

        public void Initialize() => RefreshWatchers();

        public void RefreshWatchers()
        {
            StopWatching();

            var folders = _dbHelper.GetMusicFolders();
            foreach (var path in folders)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var watcher = new GentleFolderWatcher(path, 2000);
                        watcher.FolderChanged += async (s, e) => 
                        {
                            // 2秒变动安定期结束后，静默触发增量扫库
                            await _playerService.Value.ScanMusicFoldersAsync();
                        };
                        watcher.Start();
                        _watchers.Add(watcher);
                    }
                    catch { }
                }
            }
        }

        public void StopWatching()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }
}
