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
    /// 当循环点发生变动时发�?    /// </summary>
    public class LoopPointsChangedEvent : PubSubEvent<(long Start, long End)> { }
}

