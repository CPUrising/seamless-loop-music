using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Events;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;

namespace seamless_loop_music.Services
{
    public interface IAppStateService
    {
        Task SaveCurrentStateAsync();
        Task RestoreStateAsync();
        
        // 记录当前的分类上下文
        CategoryItem CurrentCategory { get; set; }

        // 托盘行为设置
        bool MinimizeToTray { get; set; }
        bool CloseToTray { get; set; }
        bool IsExiting { get; set; }
    }

    public class AppStateService : IAppStateService
    {
        private readonly IDatabaseHelper _db;
        private readonly IPlaybackService _playbackService;
        private readonly ITrackRepository _trackRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaylistManager _playlistManager;
        
        public CategoryItem CurrentCategory { get; set; }
        
        public bool MinimizeToTray { get; set; }
        public bool CloseToTray { get; set; }
        public bool IsExiting { get; set; }

        public AppStateService(
            IDatabaseHelper db, 
            IPlaybackService playbackService, 
            ITrackRepository trackRepository, 
            IEventAggregator eventAggregator,
            IPlaylistManager playlistManager)
        {
            _db = db;
            _playbackService = playbackService;
            _trackRepository = trackRepository;
            _eventAggregator = eventAggregator;
            _playlistManager = playlistManager;

            // 监听分类选择事件，实时记录上下文
            _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Subscribe(item => 
            {
                CurrentCategory = item;
            });
        }

        public async Task SaveCurrentStateAsync()
        {
            await Task.Run(() => 
            {
                try
                {
                    // 1. 保存音量
                    _db.SetSetting("Playback.Volume", _playbackService.Volume.ToString());

                    // 2. 保存分类上下文 (从播放服务获取实际播放的上下文，确保队列还原准确)
                    var contextCategory = _playbackService.CurrentCategory;
                    if (contextCategory != null)
                    {
                        _db.SetSetting("Playback.LastCategoryType", ((int)contextCategory.Type).ToString());
                        _db.SetSetting("Playback.LastCategoryId", contextCategory.Id.ToString());
                        _db.SetSetting("Playback.LastCategoryName", contextCategory.Name ?? "");
                    }

                    // 3. 保存当前播放曲目
                    if (_playbackService.CurrentTrack != null)
                    {
                        _db.SetSetting("Playback.LastTrackId", _playbackService.CurrentTrack.Id.ToString());
                    }
                    
                    // 4. 保存播放模式
                    _db.SetSetting("Playback.PlayMode", ((int)_playbackService.PlayMode).ToString());

                    // 5. 保存循环开关状态
                    _db.SetSetting("Playback.IsSeamlessLoopEnabled", _playbackService.IsSeamlessLoopEnabled.ToString());
                    _db.SetSetting("Playback.IsFeatureLoopEnabled", _playbackService.IsFeatureLoopEnabled.ToString());

                    // 6. 保存语言设置
                    _db.SetSetting("App.Language", LocalizationService.Instance.CurrentCulture.Name);

                    // 7. 保存托盘行为设置
                    _db.SetSetting("App.MinimizeToTray", MinimizeToTray.ToString());
                    _db.SetSetting("App.CloseToTray", CloseToTray.ToString());

                    // 8. 保存精确播放队列 (参考 Dopamine 方案)
                    var queueTrackIds = _playbackService.Queue?.Select(t => t.Id).ToList();
                    _db.SaveQueuedTracks(queueTrackIds);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveState Error] {ex.Message}");
                }
            });
        }

        public async Task RestoreStateAsync()
        {
            try
            {
                // 1. 恢复音量
                string volStr = _db.GetSetting("Playback.Volume");
                if (float.TryParse(volStr, out float vol))
                {
                    _playbackService.Volume = vol;
                }

                // 2. 恢复播放模式
                string modeStr = _db.GetSetting("Playback.PlayMode");
                if (int.TryParse(modeStr, out int mode))
                {
                    _playbackService.PlayMode = (PlayMode)mode;
                }

                // 2.1 恢复循环开关状态
                _playbackService.IsSeamlessLoopEnabled = _db.GetSetting("Playback.IsSeamlessLoopEnabled", "True").ToLower() == "true";
                _playbackService.IsFeatureLoopEnabled = _db.GetSetting("Playback.IsFeatureLoopEnabled", "True").ToLower() == "true";

                // 2.2 恢复语言设置
                string langStr = _db.GetSetting("App.Language");
                if (!string.IsNullOrEmpty(langStr))
                {
                    try
                    {
                        var culture = new System.Globalization.CultureInfo(langStr);
                        LocalizationService.Instance.CurrentCulture = culture;
                    }
                    catch { }
                }

                // 2.3 恢复托盘行为设置
                MinimizeToTray = _db.GetSetting("App.MinimizeToTray", "False").ToLower() == "true";
                CloseToTray = _db.GetSetting("App.CloseToTray", "False").ToLower() == "true";

                // 3. 恢复分类上下文 (仅作为上下文记录，主要用于确定恢复后的 UI 逻辑)
                CategoryItem savedCategory = null;
                string typeStr = _db.GetSetting("Playback.LastCategoryType");
                if (!string.IsNullOrEmpty(typeStr) && int.TryParse(typeStr, out int typeInt))
                {
                    int.TryParse(_db.GetSetting("Playback.LastCategoryId", "0"), out int categoryId);
                    savedCategory = new CategoryItem
                    {
                        Type = (CategoryType)typeInt,
                        Id = categoryId,
                        Name = _db.GetSetting("Playback.LastCategoryName", "")
                    };
                }

                // 4. 重建播放队列 (精确还原，不再依赖分类过滤逻辑)
                var savedTrackIds = await Task.Run(() => _db.GetQueuedTrackIds());
                List<MusicTrack> restoredQueue = new List<MusicTrack>();
                if (savedTrackIds.Any())
                {
                    var allTracks = await _trackRepository.GetAllAsync();
                    var trackMap = allTracks.ToDictionary(t => t.Id);
                    foreach (var id in savedTrackIds)
                    {
                        if (trackMap.TryGetValue(id, out var t)) restoredQueue.Add(t);
                    }
                }

                // 5. 恢复最后播放的曲目并挂载队列
                string trackIdStr = _db.GetSetting("Playback.LastTrackId");
                if (int.TryParse(trackIdStr, out int trackId))
                {
                    var track = await _trackRepository.GetByIdAsync(trackId);
                    if (track != null)
                    {
                        // 5.1 加载曲目
                        await _playbackService.LoadTrackAsync(track, false);

                        // 5.2 挂载队列
                        if (restoredQueue.Any())
                        {
                            _playbackService.SetQueue(restoredQueue, track, savedCategory);
                        }
                    }
                }

                // 6. 界面强制返回 Rating 歌单 (我的收藏)，且不影响后台播放队列
                var ratingCategory = new CategoryItem 
                { 
                    Id = -2, 
                    Type = CategoryType.Playlist, 
                    Name = LocalizationService.Instance["PlaylistFavorites"] 
                };
                CurrentCategory = ratingCategory;
                _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Publish(ratingCategory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RestoreState Error] {ex.Message}");
            }
        }
    }
}
