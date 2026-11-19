using NUnit.Framework;
using seamless_loop_music.Services;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class LoopPointReconcilerTests
    {
        private const long StoredTotal = 18245760;
        private const long RealTotal = 34157544;

        [Test]
        public void Reconcile_DefaultWholeFileLoopAlignsToRealEnd()
        {
            long start, end;
            bool changed = LoopPointReconciler.Reconcile(StoredTotal, 0, StoredTotal, RealTotal, out start, out end);

            Assert.That(changed, Is.True);
            Assert.That(start, Is.EqualTo(0));
            Assert.That(end, Is.EqualTo(RealTotal));
        }

        [Test]
        public void Reconcile_PreservesUserDefinedLoop()
        {
            long start, end;
            bool changed = LoopPointReconciler.Reconcile(StoredTotal, 1000, 500000, RealTotal, out start, out end);

            Assert.That(changed, Is.True, "总数不一致需要调用方更新 TotalSamples");
            Assert.That(start, Is.EqualTo(1000));
            Assert.That(end, Is.EqualTo(500000));
        }

        [Test]
        public void Reconcile_ResetsOutOfRangeLoopStart()
        {
            long start, end;
            LoopPointReconciler.Reconcile(StoredTotal, StoredTotal + 5000, 20000, RealTotal, out start, out end);

            Assert.That(start, Is.EqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
        }

        [Test]
        public void Reconcile_ClampsLoopEndBeyondRealTotal()
        {
            long start, end;
            bool changed = LoopPointReconciler.Reconcile(RealTotal, 100, RealTotal + 10000, RealTotal, out start, out end);

            Assert.That(changed, Is.True);
            Assert.That(start, Is.EqualTo(100));
            Assert.That(end, Is.EqualTo(RealTotal));
        }

        [Test]
        public void Reconcile_NoChangeWhenValuesConsistent()
        {
            long start, end;
            bool changed = LoopPointReconciler.Reconcile(RealTotal, 1000, 500000, RealTotal, out start, out end);

            Assert.That(changed, Is.False);
            Assert.That(start, Is.EqualTo(1000));
            Assert.That(end, Is.EqualTo(500000));
        }

        [Test]
        public void Reconcile_IgnoresUnknownRealTotal()
        {
            long start, end;
            bool changed = LoopPointReconciler.Reconcile(StoredTotal, 0, StoredTotal, 0, out start, out end);

            Assert.That(changed, Is.False);
            Assert.That(start, Is.EqualTo(0));
            Assert.That(end, Is.EqualTo(StoredTotal));
        }
    }
}
