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
}
