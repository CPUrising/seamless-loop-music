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
        private long _durationMs;
        private string _artist;
        private string _album;
        private string _albumArtist;
        private int _rating;
        private string _coverPath;
        private string _albumCoverPath;
        private string _artistCoverPath;
        private bool _isPlaying;

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

        /// <summary>
        /// 曲目时长（毫秒），由 TagLib 或 NAudio 计算填充
        /// </summary>
        public long DurationMs
        {
            get => _durationMs;
            set { _durationMs = value; OnPropertyChanged(); }
        }

        public string Artist 
        { 
            get => _artist; 
            set { _artist = value; OnPropertyChanged(); } 
        }

        public string Album 
        { 
            get => _album; 
            set { _album = value; OnPropertyChanged(); } 
        }

        public string AlbumArtist 
        { 
            get => _albumArtist; 
            set { _albumArtist = value; OnPropertyChanged(); } 
        }

        public int Rating
        {
            get => _rating;
            set { _rating = value; OnPropertyChanged(); }
        }

        public string CoverPath
        {
            get => _coverPath;
            set 
            { 
                if (SetProperty(ref _coverPath, value)) 
                    OnPropertyChanged(nameof(EffectiveCoverPath)); 
            }
        }

        public string AlbumCoverPath
        {
            get => _albumCoverPath;
            set 
            { 
                if (SetProperty(ref _albumCoverPath, value)) 
                    OnPropertyChanged(nameof(EffectiveCoverPath)); 
            }
        }

        public string ArtistCoverPath
        {
            get => _artistCoverPath;
            set 
            { 
                if (SetProperty(ref _artistCoverPath, value)) 
                    OnPropertyChanged(nameof(EffectiveCoverPath)); 
            }
        }

        public string EffectiveCoverPath
        {
            get
            {
                // 优先级：歌曲自身封面优先 > 专辑封面保底 > 歌手封面保底，且物理文件必须存在
                if (!string.IsNullOrEmpty(CoverPath) && System.IO.File.Exists(CoverPath))
                    return CoverPath;
                if (!string.IsNullOrEmpty(AlbumCoverPath) && System.IO.File.Exists(AlbumCoverPath))
                    return AlbumCoverPath;
                if (!string.IsNullOrEmpty(ArtistCoverPath) && System.IO.File.Exists(ArtistCoverPath))
                    return ArtistCoverPath;
                return null;
            }
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        public DateTime LastModified { get; set; } = DateTime.Now;

        /// <summary>
        /// 存储循环分析引擎计算的所有候选点 (JSON 格式)
        /// </summary>
        public string LoopCandidatesJson { get; set; }

        // 辅助属性：仅供 UI 显示
        public string Title => string.IsNullOrEmpty(DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : DisplayName;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

