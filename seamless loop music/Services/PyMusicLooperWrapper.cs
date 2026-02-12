using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 基于 uvx 模式：自动管理并调用 PyMusicLooper 正式版，无需本地源码
    /// </summary>
    public class PyMusicLooperWrapper
    {
        // 使用 uvx 作为执行器，即使其他用户没装 pymusiclooper 也能自动下载运行
        public string ExecutorPath { get; set; } = "uvx"; 

        /// <summary>
        /// 调用 PyMusicLooper 寻找最佳循环点
        /// </summary>
        public async Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            // 构造 uvx 指令 (通过终端自动获取并运行最稳定的正式版)
            var args = $"pymusiclooper export-points --path \"{filePath}\" --export-to STDOUT --alt-export-top 1 --fmt SAMPLES";

            var startInfo = new ProcessStartInfo
            {
                FileName = ExecutorPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true, 
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8 
            };

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    // 直接读取全部输出
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());

                    if (process.ExitCode != 0) return null;

                    return ParseOutput(output);
                }
            }
            catch (Exception)
            {
                // 如果用户没装 uv，可以在这里尝试回退到直接调用 "pymusiclooper"
                return await TryFallbackAsync(filePath, args);
            }
        }

        private async Task<(long Start, long End, double Score)?> TryFallbackAsync(string filePath, string args)
        {
            try {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pymusiclooper",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    if (process.ExitCode == 0) return ParseOutput(output);
                }
            } catch { }
            return null;
        }

        private (long Start, long End, double Score)? ParseOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;

            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        if (long.TryParse(parts[0], out long start) &&
                            long.TryParse(parts[1], out long end) &&
                            double.TryParse(parts[4], out double score))
                        {
                            return (start, end, score);
                        }
                    }
                }
            }
            return null;
        }
    }
}
