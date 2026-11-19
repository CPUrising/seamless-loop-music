using System;
using System.IO;
using NUnit.Framework;
using seamless_loop_music;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class AudioStreamFactoryTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "AudioStreamFactoryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            }
            catch
            {
                // 清理失败不影响断言结论
            }
        }

        // 生成 0.2 秒 44100Hz 单声道 16bit PCM 的合法 WAV
        private string WriteWav(string fileName)
        {
            string path = Path.Combine(_dir, fileName);
            const int sampleRate = 44100;
            int frames = sampleRate / 5;
            int dataLength = frames * 2;

            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);

                for (int i = 0; i < frames; i++)
                {
                    writer.Write((short)(8000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate)));
                }
            }

            return path;
        }

        private string WriteFakeHeader(string fileName, byte[] header)
        {
            string path = Path.Combine(_dir, fileName);
            File.WriteAllBytes(path, header);
            return path;
        }

        [Test]
        public void DetectContainerKind_RecognizesM4a()
        {
            // 与问题文件头一致：00 00 00 1C 66 74 79 70 4D 34 41 20
            var path = WriteFakeHeader("fake.mp3", new byte[]
            {
                0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20
            });

            Assert.That(AudioStreamFactory.DetectContainerKind(path), Is.EqualTo(AudioContainerKind.AudioFile));
        }

        [Test]
        public void DetectContainerKind_RecognizesWaveFlacOggMp3()
        {
            var wave = WriteFakeHeader("a.bin", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 });
            var flac = WriteFakeHeader("b.bin", new byte[] { 0x66, 0x4C, 0x61, 0x43, 0, 0, 0, 0, 0, 0, 0, 0 });
            var ogg = WriteFakeHeader("c.bin", new byte[] { 0x4F, 0x67, 0x67, 0x53, 0, 0, 0, 0, 0, 0, 0, 0 });
            var mp3 = WriteFakeHeader("d.bin", new byte[] { 0x49, 0x44, 0x33, 0x03, 0, 0, 0, 0, 0, 0, 0, 0 });

            Assert.That(AudioStreamFactory.DetectContainerKind(wave), Is.EqualTo(AudioContainerKind.Wave));
            Assert.That(AudioStreamFactory.DetectContainerKind(flac), Is.EqualTo(AudioContainerKind.Flac));
            Assert.That(AudioStreamFactory.DetectContainerKind(ogg), Is.EqualTo(AudioContainerKind.Ogg));
            Assert.That(AudioStreamFactory.DetectContainerKind(mp3), Is.EqualTo(AudioContainerKind.Mp3));
        }

        [Test]
        public void TryCreate_WavFileReturnsWaveFileReader()
        {
            var path = WriteWav("normal.wav");

            using (var reader = AudioStreamFactory.TryCreate(path))
            {
                Assert.That(reader, Is.Not.Null);
                Assert.That(reader, Is.TypeOf<NAudio.Wave.WaveFileReader>());
                Assert.That(reader.WaveFormat.SampleRate, Is.EqualTo(44100));
            }
        }

        [Test]
        public void TryCreate_MismatchedExtensionFallsBackToSniffedReader()
        {
            // 内容为 WAV，扩展名为 .flac：扩展名解码器必然失败，应回退到 WaveFileReader
            var path = WriteWav("mismatch.flac");

            using (var reader = AudioStreamFactory.TryCreate(path))
            {
                Assert.That(reader, Is.Not.Null);
                Assert.That(reader, Is.TypeOf<NAudio.Wave.WaveFileReader>());
            }
        }

        [Test]
        public void TryCreate_UnknownExtensionUsesSniffing()
        {
            var path = WriteWav("noext.bin");

            using (var reader = AudioStreamFactory.TryCreate(path))
            {
                Assert.That(reader, Is.Not.Null);
                Assert.That(reader, Is.TypeOf<NAudio.Wave.WaveFileReader>());
            }
        }

        [Test]
        public void TryCreate_UnknownContentReturnsNull()
        {
            var path = WriteFakeHeader("garbage.xyz", new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C });

            Assert.That(AudioStreamFactory.TryCreate(path), Is.Null);
        }

        [Test]
        public void TryCreate_MissingFileReturnsNull()
        {
            Assert.That(AudioStreamFactory.TryCreate(Path.Combine(_dir, "not_exists.mp3")), Is.Null);
        }

        [Test]
        public void ThreadAffineWaveStream_ForwardsAllAccessAndDisposes()
        {
            var path = WriteWav("wrapped.wav");
            bool innerDisposed = false;

            var stream = new ThreadAffineWaveStream(() =>
            {
                var inner = new NAudio.Wave.WaveFileReader(path);
                return new DisposeTrackingStream(inner, () => innerDisposed = true);
            });

            Assert.That(stream.WaveFormat.SampleRate, Is.EqualTo(44100));
            long length = stream.Length;
            Assert.That(length, Is.GreaterThan(0));

            var buffer = new byte[4096];
            Assert.That(stream.Read(buffer, 0, buffer.Length), Is.GreaterThan(0));
            Assert.That(stream.Position, Is.GreaterThan(0));

            stream.Position = 0;
            Assert.That(stream.Position, Is.EqualTo(0));

            stream.Dispose();
            Assert.That(innerDisposed, Is.True);
        }

        [Test]
        public void ThreadAffineWaveStream_PropagatesStartupFailure()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ThreadAffineWaveStream(() => throw new InvalidOperationException("boom")));
        }

        private sealed class DisposeTrackingStream : NAudio.Wave.WaveStream
        {
            private readonly NAudio.Wave.WaveStream _inner;
            private readonly Action _onDispose;

            public DisposeTrackingStream(NAudio.Wave.WaveStream inner, Action onDispose)
            {
                _inner = inner;
                _onDispose = onDispose;
            }

            public override NAudio.Wave.WaveFormat WaveFormat => _inner.WaveFormat;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _onDispose();
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
