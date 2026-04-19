using System;
using System.Collections.Generic;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IQueueManager
    {
        IReadOnlyList<MusicTrack> Queue { get; }
        int CurrentIndex { get; }

        event Action QueueChanged;

        void SetQueue(IEnumerable<MusicTrack> tracks, MusicTrack currentTrack = null);
        void AddToQueue(MusicTrack track);
        void AddToQueue(IEnumerable<MusicTrack> tracks);
        void RemoveFromQueue(int index);
        void ClearQueue();

        MusicTrack GetCurrentTrack();
        MusicTrack GetNextTrack();
        MusicTrack GetPreviousTrack();
        MusicTrack GetTrackAt(int index);

        void MoveTo(int fromIndex, int toIndex);
    }
}
