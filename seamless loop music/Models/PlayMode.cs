namespace seamless_loop_music.Models
{
    /// <summary>
    /// 播放模式
    /// </summary>
    public enum PlayMode
    {
        SingleLoop,   // 单曲无缝循环 (默认)
        ListLoop,     // 列表循环 (设定循环次数后切换)
        Shuffle       // 随机播放
    }
}
