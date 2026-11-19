namespace seamless_loop_music.Models
{
    /// <summary>
    /// 单首曲目的分类结果（特征值 + 四象限标签 + 置信标记），仅用于展示，不落库。
    /// </summary>
    public class TrackClassificationResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }

        public double Bpm { get; set; }
        public double RmsEnergy { get; set; }
        public double SpectralCentroid { get; set; }
        public double OnsetDensity { get; set; }

        public MusicCategory Category { get; set; }

        /// <summary>BPM 置信度低于阈值时为 true，标签仅供参考。</summary>
        public bool IsUncertain { get; set; }

        /// <summary>分析是否成功；失败时 Category 为 Unknown。</summary>
        public bool Succeeded { get; set; }

        /// <summary>失败原因（成功时为空）。</summary>
        public string Error { get; set; }
    }
}
