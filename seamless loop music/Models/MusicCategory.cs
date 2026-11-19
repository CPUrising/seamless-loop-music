namespace seamless_loop_music.Models
{
    /// <summary>
    /// 双维四象限节奏/情绪分类标签（粗糙分类，无机器学习）。
    /// </summary>
    public enum MusicCategory
    {
        /// <summary>BPM 高 + 能量/亮度高。</summary>
        Intense,

        /// <summary>BPM 高 + 能量/亮度低。</summary>
        Bright,

        /// <summary>BPM 低 + 能量/亮度低。</summary>
        Calm,

        /// <summary>BPM 低 + 能量/亮度高。</summary>
        Steady,

        /// <summary>无法判定（特征缺失）。</summary>
        Unknown
    }
}
