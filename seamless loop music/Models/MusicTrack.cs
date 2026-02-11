using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace seamless_loop_music.Models
{
    /// <summary>
    /// 音乐轨道模型，用于 SQLite 存储 and UI 列表显示
    /// </summary>
    public class MusicTrack : INotifyPropertyChanged
    {
        private int _id;
        private string _displayName;
        private long _loopStart;
        private long _loopEnd;
        private long _totalSamples;

        public int Id 
        { 
            get => _id; 
            set { _id = value; OnPropertyChanged(); } 
        }

        public string FilePath { get; set; }
        public string FileName { get; set; }

        public string DisplayName 
        { 
            get => _displayName; 
            set { _displayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); } 
        }

        public long LoopStart 
        { 
            get => _loopStart; 
            set { _loopStart = value; OnPropertyChanged(); } 
        }

        public long LoopEnd 
        { 
            get => _loopEnd; 
            set { _loopEnd = value; OnPropertyChanged(); } 
        }

        public long TotalSamples 
        { 
            get => _totalSamples; 
            set { _totalSamples = value; OnPropertyChanged(); } 
        }

        public DateTime LastModified { get; set; } = DateTime.Now;

        // 辅助属性：仅供 UI 显示
        public string Title => string.IsNullOrEmpty(DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : DisplayName;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
