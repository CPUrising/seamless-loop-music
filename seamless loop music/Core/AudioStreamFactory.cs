using System;
using System.IO;
using NAudio.Flac;
using NAudio.Wave;
using NAudio.Vorbis;

namespace seamless_loop_music
{
    /// <summary>
    /// 文件头嗅探出的容器类型。
    /// </summary>
    public enum AudioContainerKind
    {
        Unknown,
        Wave,
        Flac,
        Ogg,
        Mp3,
        /// <summary>MP4/M4A、WMA、AIFF 等由 AudioFileReader 统一处理的容器。</summary>
        AudioFile
    }

    /// <summary>
    /// 音频流工厂：按扩展名优先选择解码器，失败时按文件头嗅探回退。
    /// 三处调用点（播放、扫描采样数、元数据采样率）必须共用，避免解码基准漂移。
    /// </summary>
    public static class AudioStreamFactory
    {
        private const int HeaderLength = 12;

        /// <summary>
        /// 创建音频流；失败返回 null，由调用方给出提示。
        /// </summary>
        public static WaveStream TryCreate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var byExtensionKind = KindFromExtension(filePath);
            if (byExtensionKind != AudioContainerKind.Unknown)
            {
                var stream = TryCreateByKind(byExtensionKind, filePath);
                if (stream != null)
                {
                    return stream;
                }
            }

            // 扩展名与真实容器不一致：按文件头重试
            var sniffedKind = DetectContainerKind(filePath);
            if (sniffedKind != AudioContainerKind.Unknown && sniffedKind != byExtensionKind)
            {
                var stream = TryCreateByKind(sniffedKind, filePath);
                if (stream != null)
                {
                    return stream;
                }
            }

            // 最后兜底：交给 AudioFileReader 自行判定
            if (byExtensionKind != AudioContainerKind.AudioFile && sniffedKind != AudioContainerKind.AudioFile)
            {
                return TryCreateByKind(AudioContainerKind.AudioFile, filePath);
            }

            return null;
        }

        /// <summary>
        /// 读取文件头判定真实容器；无法判定或读取失败返回 <see cref="AudioContainerKind.Unknown"/>。
        /// </summary>
        public static AudioContainerKind DetectContainerKind(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return AudioContainerKind.Unknown;
            }

            var header = new byte[HeaderLength];
            int read;
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    read = stream.Read(header, 0, header.Length);
                }
            }
            catch
            {
                return AudioContainerKind.Unknown;
            }

            if (read < 4)
            {
                return AudioContainerKind.Unknown;
            }

            if (read >= 8 && IsAscii(header, 4, "ftyp"))
            {
                return AudioContainerKind.AudioFile;
            }
            if (IsAscii(header, 0, "RIFF"))
            {
                return AudioContainerKind.Wave;
            }
            if (IsAscii(header, 0, "fLaC"))
            {
                return AudioContainerKind.Flac;
            }
            if (IsAscii(header, 0, "OggS"))
            {
                return AudioContainerKind.Ogg;
            }
            if (read >= 12 && IsAscii(header, 0, "FORM") && IsAscii(header, 8, "AIFF"))
            {
                return AudioContainerKind.AudioFile;
            }
            if (IsAscii(header, 0, "ID3"))
            {
                return AudioContainerKind.Mp3;
            }
            if (read >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            {
                return AudioContainerKind.Mp3;
            }

            return AudioContainerKind.Unknown;
        }

        private static AudioContainerKind KindFromExtension(string filePath)
        {
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".wav":
                case ".wave":
                    return AudioContainerKind.Wave;
                case ".flac":
                    return AudioContainerKind.Flac;
                case ".ogg":
                    return AudioContainerKind.Ogg;
                case ".mp3":
                    return AudioContainerKind.Mp3;
                case ".m4a":
                case ".wma":
                case ".aiff":
                case ".opus":
                    return AudioContainerKind.AudioFile;
                default:
                    return AudioContainerKind.Unknown;
            }
        }

        private static WaveStream TryCreateByKind(AudioContainerKind kind, string filePath)
        {
            try
            {
                switch (kind)
                {
                    case AudioContainerKind.Wave:
                        return new WaveFileReader(filePath);
                    case AudioContainerKind.Flac:
                        // 使用 bundled NAudio FLAC reader，避免依赖 Windows 系统解码器。
                        return new FlacReader(filePath);
                    case AudioContainerKind.Ogg:
                        return new VorbisWaveReader(filePath);
                    case AudioContainerKind.Mp3:
                        return new Mp3FileReader(filePath);
                    case AudioContainerKind.AudioFile:
                        // AudioFileReader 处理 m4a/wma 时依赖 Media Foundation，其 COM 对象不能跨线程访问，
                        // 因此固定到专用线程（播放时的填充线程与 UI 线程都会访问同一音频流）。
                        return new ThreadAffineWaveStream(() => new AudioFileReader(filePath));
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAscii(byte[] buffer, int offset, string ascii)
        {
            if (buffer.Length < offset + ascii.Length)
            {
                return false;
            }

            for (int i = 0; i < ascii.Length; i++)
            {
                if (buffer[offset + i] != (byte)ascii[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
