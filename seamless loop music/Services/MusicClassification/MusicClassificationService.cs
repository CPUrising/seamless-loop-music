using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Models;
using seamless_loop_music.Services.LoopFinder;

namespace seamless_loop_music.Services.MusicClassification
{
    /// <summary>
    /// 双维四象限节奏/情绪分类。
    /// 特征提取走原生 loopfinder；分类规则为纯函数（见 <see cref="Classify"/>），可单测、阈值可调。
    /// </summary>
    public class MusicClassificationService : IMusicClassificationService
    {
        // ---- 可集中调整的规则常量 ----

        /// <summary>BPM 默认分界（样本不足/退化时兜底）。</summary>
        public static double DefaultBpmThreshold = 120.0;

        /// <summary>BPM 置信度低于该值时标记"不确定"。</summary>
        public static double ConfidenceThreshold = 0.5;

        /// <summary>自适应阈值所需的最小样本数；不足则全部用默认阈值。</summary>
        public static int MinSamplesForAdaptive = 4;

        private static readonly string[] SupportedExtensions =
            { ".mp3", ".flac", ".wav", ".ogg" };

        private readonly Native _native;

        public event Action<string> OnStatusMessage
        {
            add { _native.OnStatusMessage += value; }
            remove { _native.OnStatusMessage -= value; }
        }

        public MusicClassificationService()
        {
            _native = new Native();
        }

        public async Task<List<TrackClassificationResult>> ClassifyFolderAsync(
            string folderPath,
            bool recursive,
            IProgress<(int current, int total, string fileName)> progress,
            CancellationToken ct)
        {
            var results = new List<TrackClassificationResult>();
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return results;

            var files = Directory
                .EnumerateFiles(folderPath, "*.*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => SupportedExtensions.Contains(
                    Path.GetExtension(f)?.ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = files.Count;

            // 逐文件提取特征（原生调用放后台线程）
            var features = new List<(string path, TrackFeatures f, string err)>();
            for (int i = 0; i < total; ++i)
            {
                ct.ThrowIfCancellationRequested();
                var path = files[i];
                progress?.Report((i + 1, total, Path.GetFileName(path)));

                var lf = await Task.Run(() => _native.ClassifyFile(path), ct);
                if (lf.HasValue)
                {
                    var v = lf.Value;
                    features.Add((path, new TrackFeatures
                    {
                        Bpm = v.bpm,
                        BpmConfidence = v.bpmConfidence,
                        OnsetDensity = v.onsetDensity,
                        RmsEnergy = v.rmsEnergy,
                        SpectralCentroid = v.spectralCentroid,
                        DurationSamples = v.durationSamples,
                        SampleRate = v.sampleRate
                    }, null));
                }
                else
                {
                    features.Add((path, null, "decode or feature extraction failed"));
                }
            }

            // 自适应阈值：用本批次成功样本的中位数；样本不足用默认兜底
            var ok = features.Where(x => x.f != null).Select(x => x.f).ToList();
            double bpmThr = ok.Count >= MinSamplesForAdaptive
                ? Median(ok.Select(f => f.Bpm))
                : DefaultBpmThreshold;
            double energyThr = ok.Count >= MinSamplesForAdaptive
                ? Median(ok.Select(f => f.RmsEnergy))
                : 0.0; // 能量无默认物理阈值，样本不足时仅用 BPM 单轴
            double centroidThr = ok.Count >= MinSamplesForAdaptive
                ? Median(ok.Select(f => f.SpectralCentroid))
                : 0.0;

            foreach (var (path, f, err) in features)
            {
                var res = new TrackClassificationResult
                {
                    FilePath = path,
                    FileName = Path.GetFileName(path)
                };

                if (f == null)
                {
                    res.Succeeded = false;
                    res.Category = MusicCategory.Unknown;
                    res.Error = err;
                }
                else
                {
                    res.Succeeded = true;
                    res.Bpm = f.Bpm;
                    res.RmsEnergy = f.RmsEnergy;
                    res.SpectralCentroid = f.SpectralCentroid;
                    res.OnsetDensity = f.OnsetDensity;
                    var (cat, uncertain) = Classify(f, bpmThr, energyThr, centroidThr);
                    res.Category = cat;
                    res.IsUncertain = uncertain;
                }

                results.Add(res);
            }

            return results;
        }

        /// <summary>
        /// 纯函数分类规则：给定特征与阈值，输出四象限标签与置信标记。
        /// 快慢轴 = BPM；激进度轴 = 能量与频谱质心归一化后的合成得分。
        /// </summary>
        public static (MusicCategory category, bool uncertain) Classify(
            TrackFeatures f, double bpmThreshold, double energyThreshold, double centroidThreshold)
        {
            if (f == null) return (MusicCategory.Unknown, true);

            bool fast = f.Bpm >= bpmThreshold;

            // 激进度：能量与亮度各占一半，分别按各自阈值归一化为 [-1,1] 再求均值。
            // 阈值为 0（样本不足）时该子项不计入，退化为单轴。
            double score = 0.0;
            int axes = 0;
            if (energyThreshold > 0.0)
            {
                score += Normalize(f.RmsEnergy, energyThreshold);
                axes++;
            }
            if (centroidThreshold > 0.0)
            {
                score += Normalize(f.SpectralCentroid, centroidThreshold);
                axes++;
            }
            bool intense = axes > 0 ? (score / axes) >= 0.0 : false;

            MusicCategory cat;
            if (fast && intense) cat = MusicCategory.Intense;
            else if (fast && !intense) cat = MusicCategory.Bright;
            else if (!fast && !intense) cat = MusicCategory.Calm;
            else cat = MusicCategory.Steady;

            bool uncertain = f.BpmConfidence < ConfidenceThreshold;
            return (cat, uncertain);
        }

        // 以阈值为中心把值映射到 (-∞,∞)：等于阈值→0，翻倍→约+0.5，一半→约-1
        private static double Normalize(double value, double threshold)
        {
            if (threshold <= 0.0) return 0.0;
            return (value - threshold) / threshold;
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            if (n == 0) return 0.0;
            return (n % 2 == 1) ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
