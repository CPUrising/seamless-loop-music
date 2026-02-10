using System;

namespace seamless_loop_music.Models
{
    /// <summary>
    /// 音乐轨道模型，用于 SQLite 存储和 UI 列表显示
    /// </summary>
    public class MusicTrack
    {
        public int Id { get; set; }

        /// <summary>
        /// 完整文件路径（用于内部加载）
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// 文件名（用于数据库持久化，作为指纹一部分）
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 显示名称（别名系统，默认通常为文件名）
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 循环起始点（采样数）
        /// </summary>
        public long LoopStart { get; set; }

        /// <summary>
        /// 循环结束点（采样数，0 表示全曲）
        /// </summary>
        public long LoopEnd { get; set; }

        /// <summary>
        /// 音量倍数 (0.0 - 1.0)
        /// </summary>
        public double Volume { get; set; } = 1.0;

        /// <summary>
        /// 总采样数（用于校验配置是否匹配）
        /// </summary>
        public long TotalSamples { get; set; }

        public DateTime LastModified { get; set; } = DateTime.Now;

        // 辅助属性：仅供 UI 显示
        public string Title => string.IsNullOrEmpty(DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : DisplayName;
    }
}
