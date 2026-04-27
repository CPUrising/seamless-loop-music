using Prism.Events;
using seamless_loop_music.Models;

namespace seamless_loop_music.Events
{
    /// <summary>
    /// 当音轨加载完成时发布
    /// </summary>
    public class TrackLoadedEvent : PubSubEvent<MusicTrack> { }

    /// <summary>
    /// 当播放状态改变时发布
    /// </summary>
    public class PlaybackStateChangedEvent : PubSubEvent<NAudio.Wave.PlaybackState> { }
    
    /// <summary>
    /// 当循环点发生变动时发布
    /// </summary>
    public class LoopPointsChangedEvent : PubSubEvent<(long Start, long End)> { }

    /// <summary>
    /// 当歌单列表发生变动（新建、重命名、删除）时发布
    /// </summary>
    public class PlaylistChangedEvent : PubSubEvent { }

    /// <summary>
    /// 当曲目元数据（爱心、评分）发生变动时发布
    /// </summary>
    public class TrackMetadataChangedEvent : PubSubEvent<MusicTrack> { }

    /// <summary>
    /// 当音乐库扫描完成或刷新后发布，通知 UI 重新加载曲目数据
    /// </summary>
    public class LibraryRefreshedEvent : PubSubEvent { }

    /// <summary>
    /// 当系统有新的状态消息需要显示时发布
    /// </summary>
    public class StatusMessageEvent : PubSubEvent<string> { }

    /// <summary>
    /// 请求 UI 将列表滚动到指定曲目位置时发布
    /// </summary>
    public class ScrollToTrackEvent : PubSubEvent<MusicTrack> { }
}
