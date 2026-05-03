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

                    // 2. 保存分类上下文
                    if (CurrentCategory != null)
                    {
                        _db.SetSetting("Playback.LastCategoryType", ((int)CurrentCategory.Type).ToString());
                        _db.SetSetting("Playback.LastCategoryId", CurrentCategory.Id.ToString());
                        _db.SetSetting("Playback.LastCategoryName", CurrentCategory.Name ?? "");
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

                // 3. 恢复分类上下文
                string typeStr = _db.GetSetting("Playback.LastCategoryType");
                if (!string.IsNullOrEmpty(typeStr) && int.TryParse(typeStr, out int typeInt))
                {
                    int.TryParse(_db.GetSetting("Playback.LastCategoryId", "0"), out int categoryId);
                    var category = new CategoryItem
                    {
                        Type = (CategoryType)typeInt,
                        Id = categoryId,
                        Name = _db.GetSetting("Playback.LastCategoryName", "")
                    };

                    CurrentCategory = category;
                    // 发布事件通知 UI 跳转并刷新曲目列表
                    _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Publish(category);
                    
                    // 等待 UI 响应并刷新列表
                    await Task.Delay(100);
                }

                // 4. 恢复最后播放的曲目并重建队列
                string trackIdStr = _db.GetSetting("Playback.LastTrackId");
                if (int.TryParse(trackIdStr, out int trackId))
                {
                    var track = await _trackRepository.GetByIdAsync(trackId);
                    if (track != null)
                    {
                        // 4.1 加载曲目
                        await _playbackService.LoadTrackAsync(track, false);

                        // 4.2 尝试根据分类上下文还原队列，以便支持切歌
                        if (CurrentCategory != null)
                        {
                            List<MusicTrack> contextTracks = null;
                            switch (CurrentCategory.Type)
                            {
                                case CategoryType.Album:
                                    contextTracks = await _trackRepository.GetByAlbumAsync(CurrentCategory.Name);
                                    break;
                                case CategoryType.Artist:
                                    contextTracks = await _trackRepository.GetByArtistAsync(CurrentCategory.Name);
                                    break;
                                case CategoryType.Playlist:
                                    contextTracks = await _playlistManager.GetTracksInPlaylistAsync(CurrentCategory.Id);
                                    break;
                            }

                            if (contextTracks != null && contextTracks.Any())
                            {
                                _playbackService.SetQueue(contextTracks, track, CurrentCategory);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RestoreState Error] {ex.Message}");
            }
        }
    }
}
