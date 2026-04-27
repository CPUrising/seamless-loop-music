using System;
using System.Collections.Generic;
using System.Linq;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public class QueueManager : IQueueManager
    {
        private readonly List<MusicTrack> _queue = new List<MusicTrack>();
        private readonly Random _random = new Random();
        private int _currentIndex = -1;
        private PlayMode _playMode = PlayMode.ListLoop;

        public IReadOnlyList<MusicTrack> Queue => _queue.AsReadOnly();
        public int CurrentIndex => _currentIndex;
        public PlayMode PlayMode { get => _playMode; set => _playMode = value; }

        public event Action QueueChanged;

        public void SetQueue(IEnumerable<MusicTrack> tracks, MusicTrack currentTrack = null)
        {
            _queue.Clear();
            if (tracks != null)
            {
                _queue.AddRange(tracks);
            }

            if (currentTrack != null && _queue.Count > 0)
            {
                _currentIndex = _queue.FindIndex(t => t.Id == currentTrack.Id);
                if (_currentIndex < 0) _currentIndex = 0;
            }
            else
            {
                _currentIndex = _queue.Count > 0 ? 0 : -1;
            }

            QueueChanged?.Invoke();
        }

        public void AddToQueue(MusicTrack track)
        {
            if (track == null) return;
            _queue.Add(track);
            if (_currentIndex < 0) _currentIndex = 0;
            QueueChanged?.Invoke();
        }

        public void AddToQueue(IEnumerable<MusicTrack> tracks)
        {
            if (tracks == null) return;
            _queue.AddRange(tracks);
            if (_currentIndex < 0 && _queue.Count > 0) _currentIndex = 0;
            QueueChanged?.Invoke();
        }

        public void RemoveFromQueue(int index)
        {
            if (index < 0 || index >= _queue.Count) return;

            _queue.RemoveAt(index);

            if (_queue.Count == 0)
            {
                _currentIndex = -1;
            }
            else if (index < _currentIndex)
            {
                _currentIndex--;
            }
            else if (index == _currentIndex && _currentIndex >= _queue.Count)
            {
                _currentIndex = _queue.Count - 1;
            }

            QueueChanged?.Invoke();
        }

        public void ClearQueue()
        {
            _queue.Clear();
            _currentIndex = -1;
            QueueChanged?.Invoke();
        }

        public MusicTrack GetCurrentTrack()
        {
            if (_queue.Count == 0 || _currentIndex < 0 || _currentIndex >= _queue.Count)
                return null;
            return _queue[_currentIndex];
        }

        public MusicTrack GetNextTrack()
        {
            if (_queue.Count == 0) return null;

            int nextIndex;
            if (_playMode == PlayMode.Shuffle)
            {
                nextIndex = _random.Next(0, _queue.Count);
            }
            else
            {
                nextIndex = (_currentIndex + 1) % _queue.Count;
            }

            _currentIndex = nextIndex;
            QueueChanged?.Invoke();
            return _queue[nextIndex];
        }

        public MusicTrack GetPreviousTrack()
        {
            if (_queue.Count == 0) return null;

            int prevIndex = (_currentIndex - 1 + _queue.Count) % _queue.Count;
            _currentIndex = prevIndex;
            QueueChanged?.Invoke();
            return _queue[prevIndex];
        }

        public MusicTrack GetTrackAt(int index)
        {
            if (index < 0 || index >= _queue.Count) return null;
            _currentIndex = index;
            QueueChanged?.Invoke();
            return _queue[index];
        }

        public void MoveTo(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _queue.Count) return;
            if (toIndex < 0 || toIndex >= _queue.Count) return;

            var track = _queue[fromIndex];
            _queue.RemoveAt(fromIndex);
            _queue.Insert(toIndex, track);

            if (_currentIndex == fromIndex)
            {
                _currentIndex = toIndex;
            }
            else if (_currentIndex > fromIndex && _currentIndex <= toIndex)
            {
                _currentIndex--;
            }
            else if (_currentIndex < fromIndex && _currentIndex >= toIndex)
            {
                _currentIndex++;
            }

            QueueChanged?.Invoke();
        }
    }
}
