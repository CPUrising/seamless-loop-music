namespace seamless_loop_music.Models
{
    /// <summary>
    /// 纯信号处理提取的音频物理特征（无机器学习输出）。
    /// </summary>
    public class TrackFeatures
    {
        /// <summary>每分钟节拍数（aubio tempo 估算）。</summary>
        public double Bpm { get; set; }

        /// <summary>BPM 估算置信度，越高越可靠。</summary>
        public double BpmConfidence { get; set; }

        /// <summary>每秒起始（onset/节拍事件）数量。</summary>
        public double OnsetDensity { get; set; }

        /// <summary>整曲 RMS 能量 [0,1]。</summary>
        public double RmsEnergy { get; set; }

        /// <summary>频谱质心（Hz），衡量"亮度"。</summary>
        public double SpectralCentroid { get; set; }

        /// <summary>采样点数。</summary>
        public long DurationSamples { get; set; }

        /// <summary>采样率。</summary>
        public int SampleRate { get; set; }
    }
}
