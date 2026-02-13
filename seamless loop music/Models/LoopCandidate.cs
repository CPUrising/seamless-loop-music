using System;

namespace seamless_loop_music.Models
{
    public class LoopCandidate
    {
        public long LoopStart { get; set; }
        public long LoopEnd { get; set; }
        public double Score { get; set; }
        public double LoudnessDifference { get; set; }
        public double NoteDifference { get; set; }

        public override string ToString()
        {
            return $"Start: {LoopStart}, End: {LoopEnd}, Score: {Score:P2}";
        }
    }
}
