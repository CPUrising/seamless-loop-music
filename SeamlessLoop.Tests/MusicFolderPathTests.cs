using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using seamless_loop_music.Services;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class MusicFolderPathTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "MusicFolderPathTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Album"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch
            {
                // 清理失败不影响断言结论
            }
        }

        [Test]
        public void Normalize_TrimsTrailingSeparator()
        {
            Assert.That(MusicFolderPath.Normalize(@"C:\Music\"), Is.EqualTo(@"C:\Music"));
        }

        [Test]
        public void Normalize_KeepsDriveRoot()
        {
            Assert.That(MusicFolderPath.Normalize(@"C:\"), Is.EqualTo(@"C:\"));
        }

        [Test]
        public void IsSameOrAncestor_IsCaseInsensitive()
        {
            Assert.That(MusicFolderPath.IsSameOrAncestor(@"C:\Music", @"c:\music\album"), Is.True);
            Assert.That(MusicFolderPath.IsSameOrAncestor(@"C:\Music\a", @"C:\Music\A\"), Is.True);
        }

        [Test]
        public void IsSameOrAncestor_RejectsSiblingPrefix()
        {
            Assert.That(MusicFolderPath.IsSameOrAncestor(@"C:\Music", @"C:\Music2\a"), Is.False);
        }

        [Test]
        public void BuildPlan_ParentReplacesChild()
        {
            string child = Path.Combine(_root, "Album");

            var plan = MusicFolderPath.BuildPlan(new[] { child }, new[] { _root });

            Assert.That(plan.ToAdd.Count, Is.EqualTo(1));
            Assert.That(plan.ToAdd[0], Is.EqualTo(_root));
            Assert.That(plan.ToRemove, Does.Contain(child));
            Assert.That(plan.SkippedCovered.Count, Is.EqualTo(0));
            Assert.That(plan.Rejected.Count, Is.EqualTo(0));
        }

        [Test]
        public void BuildPlan_SkipsFolderCoveredByExisting()
        {
            string child = Path.Combine(_root, "Album");

            var plan = MusicFolderPath.BuildPlan(new[] { _root }, new[] { child });

            Assert.That(plan.ToAdd.Count, Is.EqualTo(0));
            Assert.That(plan.SkippedCovered.Count, Is.EqualTo(1));
            Assert.That(plan.ToRemove.Count, Is.EqualTo(0));
        }

        [Test]
        public void BuildPlan_RejectsMissingFolder()
        {
            string missing = Path.Combine(_root, "Missing_" + Guid.NewGuid().ToString("N"));

            var plan = MusicFolderPath.BuildPlan(Enumerable.Empty<string>(), new[] { missing });

            Assert.That(plan.ToAdd.Count, Is.EqualTo(0));
            Assert.That(plan.Rejected.Count, Is.EqualTo(1));
        }
    }
}
