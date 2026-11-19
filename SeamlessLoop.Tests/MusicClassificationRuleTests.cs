using NUnit.Framework;
using seamless_loop_music.Models;
using seamless_loop_music.Services.MusicClassification;

namespace SeamlessLoop.Tests
{
    /// <summary>
    /// 分类规则单元测试：只覆盖纯函数 <see cref="MusicClassificationService.Classify"/>，
    /// 不加载原生 loopfinder.dll，因此可在任意环境稳定运行。
    /// 对应 spec 场景 T03（四象限规则映射）、T04（低置信显式标记）。
    /// </summary>
    [TestFixture]
    public class MusicClassificationRuleTests
    {
        private const double BpmThreshold = 120.0;
        private const double EnergyThreshold = 0.10;
        private const double CentroidThreshold = 2000.0;

        private static TrackFeatures MakeFeatures(
            double bpm, double rmsEnergy, double centroid, double confidence = 1.0)
        {
            return new TrackFeatures
            {
                Bpm = bpm,
                BpmConfidence = confidence,
                RmsEnergy = rmsEnergy,
                SpectralCentroid = centroid
            };
        }

        // T03：快 + 高能量/亮度 → 激烈
        [Test]
        public void T03_Classify_FastHighEnergy_ReturnsIntense()
        {
            var f = MakeFeatures(bpm: 150.0, rmsEnergy: 0.30, centroid: 4000.0);

            var (category, _) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Intense));
        }

        // T03：快 + 低能量/亮度 → 明亮轻快
        [Test]
        public void T03_Classify_FastLowEnergy_ReturnsBright()
        {
            var f = MakeFeatures(bpm: 150.0, rmsEnergy: 0.03, centroid: 800.0);

            var (category, _) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Bright));
        }

        // T03：慢 + 低能量/亮度 → 舒缓
        [Test]
        public void T03_Classify_SlowLowEnergy_ReturnsCalm()
        {
            var f = MakeFeatures(bpm: 80.0, rmsEnergy: 0.03, centroid: 800.0);

            var (category, _) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Calm));
        }

        // T03：慢 + 高能量/亮度 → 沉稳
        [Test]
        public void T03_Classify_SlowHighEnergy_ReturnsSteady()
        {
            var f = MakeFeatures(bpm: 80.0, rmsEnergy: 0.30, centroid: 4000.0);

            var (category, _) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Steady));
        }

        // T03：BPM 恰好等于阈值按"快"处理（>= 语义）
        [Test]
        public void T03_Classify_BpmAtThreshold_CountsAsFast()
        {
            var f = MakeFeatures(bpm: BpmThreshold, rmsEnergy: 0.30, centroid: 4000.0);

            var (category, _) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Intense));
        }

        // T03：两个轴各占一半，单轴偏低时合成得分仍可判为"不激烈"
        [Test]
        public void T03_Classify_EnergyHighButCentroidLow_IsNotIntense()
        {
            // 能量 0.25 → +1.5；质心 1000 → -0.5；均值 +0.5 仍为激烈？
            // 构造更极端的偏离：能量 0.20(+1.0)、质心 400(-0.8) → 均值 +0.1 → 仍激烈
            // 这里取能量 0.15(+0.5)、质心 200(-0.9) → 均值 -0.2 → 不激烈
            var f = MakeFeatures(bpm: 150.0, rmsEnergy: 0.15, centroid: 200.0);

            var (category, _) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Bright));
        }

        // T03：阈值为 0（样本不足）时能量/亮度轴退化，仅按 BPM 单轴判定
        [Test]
        public void T03_Classify_DegenerateThresholds_UsesBpmAxisOnly()
        {
            var fast = MakeFeatures(bpm: 150.0, rmsEnergy: 0.30, centroid: 4000.0);
            var slow = MakeFeatures(bpm: 80.0, rmsEnergy: 0.30, centroid: 4000.0);

            var fastResult = MusicClassificationService.Classify(fast, BpmThreshold, 0.0, 0.0);
            var slowResult = MusicClassificationService.Classify(slow, BpmThreshold, 0.0, 0.0);

            Assert.That(fastResult.category, Is.EqualTo(MusicCategory.Bright));
            Assert.That(slowResult.category, Is.EqualTo(MusicCategory.Calm));
        }

        // T04：置信度低于阈值 → 仍给出标签，但显式标记不确定
        [Test]
        public void T04_Classify_LowConfidence_StillLabelledButMarkedUncertain()
        {
            var f = MakeFeatures(bpm: 150.0, rmsEnergy: 0.30, centroid: 4000.0,
                confidence: MusicClassificationService.ConfidenceThreshold - 0.1);

            var (category, uncertain) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Intense), "仍应输出分类标签");
            Assert.That(uncertain, Is.True, "低置信必须被标记");
        }

        // T04：置信度达标 → 不标记不确定
        [Test]
        public void T04_Classify_HighConfidence_NotMarkedUncertain()
        {
            var f = MakeFeatures(bpm: 150.0, rmsEnergy: 0.30, centroid: 4000.0,
                confidence: MusicClassificationService.ConfidenceThreshold + 0.1);

            var (_, uncertain) = MusicClassificationService.Classify(
                f, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(uncertain, Is.False);
        }

        // 特征缺失 → 未知且标记不确定
        [Test]
        public void Classify_NullFeatures_ReturnsUnknown()
        {
            var (category, uncertain) = MusicClassificationService.Classify(
                null, BpmThreshold, EnergyThreshold, CentroidThreshold);

            Assert.That(category, Is.EqualTo(MusicCategory.Unknown));
            Assert.That(uncertain, Is.True);
        }
    }
}
