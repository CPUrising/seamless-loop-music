using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using seamless_loop_music.Models;

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

            // 构造参数：直接用 pymusiclooper 命令
            // 注意：NET 4.8 不支持 ArgumentList，需要手动拼接字符串
            var args = $"export-points --path \"{filePath}\" --export-to STDOUT --alt-export-top 1 --fmt SAMPLES";

            try
            {
                // 1. 优先尝试直接调用系统环境中的 pymusiclooper
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pymusiclooper",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, 
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8 
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    // 并行读取 stdout 和 stderr，防止缓冲区填满导致的死锁
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    await Task.WhenAll(stdoutTask, stderrTask);
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return ParseOutput(stdoutTask.Result);
                    }
                }
            }
            catch (Exception)
            {
                // 2. 如果失败（如未安装或未加入PATH），尝试使用 uvx 作为 fallback
                // uvx pymusiclooper ...
                return await TryFallbackAsync($"pymusiclooper {args}");
            }
            
            // 如果 exit code != 0，也尝试 fallback
            return await TryFallbackAsync($"pymusiclooper {args}");
        }

        /// <summary>
        /// 获取前 N 个最佳循环点
        /// </summary>
        public async Task<List<LoopCandidate>> GetTopLoopPointsAsync(string filePath, int count = 10)
        {
            if (!File.Exists(filePath)) return new List<LoopCandidate>();

            // --alt-export-top N: 导出前 N 个循环点
            var args = $"export-points --path \"{filePath}\" --export-to STDOUT --alt-export-top {count} --fmt SAMPLES";
            string output = null;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pymusiclooper",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();
                    await Task.WhenAll(stdoutTask, stderrTask);
                    process.WaitForExit();

                    if (process.ExitCode == 0) output = stdoutTask.Result;
                }
            }
            catch
            {
                 // fallback immediately
            }

            if (string.IsNullOrEmpty(output))
            {
                // Fallback via uvx
                 output = await TryFallbackCaptureOutputAsync($"pymusiclooper {args}");
            }

            return ParseLoopCandidates(output);
        }







        private async Task<(long Start, long End, double Score)?> TryFallbackAsync(string args)
        {
            string output = await TryFallbackCaptureOutputAsync(args);
            if (!string.IsNullOrEmpty(output)) return ParseOutput(output);
            return null;
        }

        private async Task<string> TryFallbackCaptureOutputAsync(string args)
        {
            try {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "uvx", // 现在这里专门跑 uvx 作为 fallback
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // 同样需要处理 stderr
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    await Task.WhenAll(stdoutTask, stderrTask);
                    process.WaitForExit();

                    if (process.ExitCode == 0) return stdoutTask.Result;
                }
            } catch { }
            return null;
        }

        private (long Start, long End, double Score)? ParseOutput(string output)
        {
            var candidates = ParseLoopCandidates(output);
            if (candidates.Any())
            {
                var best = candidates.First();
                return (best.LoopStart, best.LoopEnd, best.Score);
            }
            return null;
        }

        private List<LoopCandidate> ParseLoopCandidates(string output)
        {
            var results = new List<LoopCandidate>();
            if (string.IsNullOrWhiteSpace(output)) return results;

            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // 格式：Start End NoteDiff LoudnessDiff Score
                    if (parts.Length >= 5)
                    {
                        if (long.TryParse(parts[0], out long start) &&
                            long.TryParse(parts[1], out long end) &&
                            double.TryParse(parts[4], out double score))
                        {
                            // 可选解析 NoteDifference 和 LoudnessDifference
                            double.TryParse(parts[2], out double noteDiff);
                            double.TryParse(parts[3], out double loudDiff);

                            results.Add(new LoopCandidate 
                            { 
                                LoopStart = start, 
                                LoopEnd = end, 
                                Score = score,
                                NoteDifference = noteDiff,
                                LoudnessDifference = loudDiff
                            });
                        }
                    }
                }
            }
            return results;
        }
    }
}
