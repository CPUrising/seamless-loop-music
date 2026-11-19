namespace seamless_loop_music.Services
{
    /// <summary>
    /// 循环点与总采样数的对齐规则。
    /// 背景：部分文件（例如扩展名与实际容器不符的音频）TagLib 给出的采样率/时长与实际解码器不一致，
    /// 数据库里的 TotalSamples 与 LoopEnd 会偏小，导致播放到中途被判为循环终点并跳回开头。
    /// 这里只做纯计算，落库由调用方负责。
    /// </summary>
    public static class LoopPointReconciler
    {
        public const long SampleCountTolerance = 1000;

        /// <summary>
        /// 用实际解码得到的采样数校正循环点。
        /// </summary>
        /// <param name="storedTotal">数据库中记录的采样数（可能来自 TagLib 的错误估算）</param>
        /// <param name="loopStart">数据库中的循环起点</param>
        /// <param name="loopEnd">数据库中的循环终点</param>
        /// <param name="realTotal">实际解码器给出的采样数</param>
        /// <param name="newStart">校正后的循环起点</param>
        /// <param name="newEnd">校正后的循环终点</param>
        /// <returns>是否需要写回数据库</returns>
        public static bool Reconcile(
            long storedTotal,
            long loopStart,
            long loopEnd,
            long realTotal,
            out long newStart,
            out long newEnd)
        {
            newStart = loopStart;
            newEnd = loopEnd;

            if (realTotal <= 0)
            {
                return false;
            }

            bool totalMismatched = storedTotal <= 0 ||
                                   System.Math.Abs(storedTotal - realTotal) > SampleCountTolerance;

            // 终点覆盖整首（默认值）或以旧总数为终点时，视为"整曲循环"，直接对齐到真实末尾
            bool loopEndCoversWholeFile = loopEnd <= 0 || loopEnd >= storedTotal;

            bool changed = false;

            if (loopEndCoversWholeFile)
            {
                if (newEnd != realTotal)
                {
                    newEnd = realTotal;
                    changed = true;
                }
            }
            else if (newEnd > realTotal)
            {
                newEnd = realTotal;
                changed = true;
            }

            // 起点越界：超出记录中的总数（说明该值本身不可信）或超出真实总数
            long startLimit = storedTotal > 0 ? System.Math.Min(storedTotal, realTotal) : realTotal;
            if (newStart < 0 || newStart >= startLimit)
            {
                newStart = 0;
                changed = true;
            }

            if (newEnd <= newStart)
            {
                newEnd = realTotal;
                changed = true;
            }

            // 总数不一致本身也需要调用方刷新 UI，因此并入返回值
            return changed || totalMismatched;
        }
    }
}
