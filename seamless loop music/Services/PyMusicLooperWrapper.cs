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
        /// 自定义下载/缓存路径 (对应 UV_CACHE_DIR)
        /// </summary>
        public string CustomCachePath { get; set; }

        /// <summary>
        /// 自定义 PyMusicLooper 可执行文件路径 (优先级最高)
        /// </summary>
        public string PyMusicLooperExecutablePath { get; set; }

        public event Action<string> OnStatusMessage;

        /// <summary>
        /// 检查环境：是否已经安装了 PyMusicLooper 或者已经在缓存中了
        /// </summary>
        /// <returns>0: 已就绪; 1: 需要下载; 2: uv 都不存在</returns>
        public async Task<int> CheckEnvironmentAsync()
        {
            // 0. 优先查手动指定的 EXE 路径
            if (!string.IsNullOrEmpty(PyMusicLooperExecutablePath) && File.Exists(PyMusicLooperExecutablePath))
            {
                if (await IsCommandAvailableAsync(PyMusicLooperExecutablePath, "--version")) return 0;
            }

            // 1. 查系统路径 (直接运行 pymusiclooper)
            if (await IsCommandAvailableAsync("pymusiclooper", "--version")) return 0;

            // 2. 查 uv 是否存在
            if (!await IsCommandAvailableAsync("uv", "--version")) return 2;

            // 3. 查 uvx 缓存 (用 --offline 探测)
            // 如果 uvx 已经下载过，offline 也能跑通获取版本
            if (await IsCommandAvailableAsync("uv", "tool run --offline pymusiclooper --version")) return 0;

            return 1; // 需要下载
        }

        private async Task<bool> IsCommandAvailableAsync(string cmd, string args)
        {
            try {
                var startInfo = new ProcessStartInfo {
                    FileName = cmd,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                // 如果设置了自定义路径，检查时也要带上，否则查不到对应的离线工具
                if (!string.IsNullOrEmpty(CustomCachePath) && cmd == "uv") {
                    startInfo.EnvironmentVariables["UV_CACHE_DIR"] = CustomCachePath;
                }

                using (var p = Process.Start(startInfo)) {
                    // 给予 5 秒超时，防止死锁
                    bool exited = await Task.Run(() => p.WaitForExit(5000));
                    return exited && p.ExitCode == 0;
                }
            } catch { return false; }
        }

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
                OnStatusMessage?.Invoke("LOC:StatusAnalyzing");
                // 1. 优先尝试直接调用系统环境中的 pymusiclooper
                var startInfo = new ProcessStartInfo
                {
                    FileName = !string.IsNullOrEmpty(PyMusicLooperExecutablePath) && File.Exists(PyMusicLooperExecutablePath) 
                               ? PyMusicLooperExecutablePath : "pymusiclooper",
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
            catch (Exception) { }
            
            // 2. 如果直接调用失败，尝试使用 uv (必须是离线可用的)
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "uv",
                    Arguments = $"tool run --offline pymusiclooper {args}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                if (!string.IsNullOrEmpty(CustomCachePath)) startInfo.EnvironmentVariables["UV_CACHE_DIR"] = CustomCachePath;

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    process.WaitForExit();
                    if (process.ExitCode == 0) return ParseOutput(output);
                }
            }
            catch { }

            return null;
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
            catch { }

            if (string.IsNullOrEmpty(output))
            {
                // Try uv offline
                try {
                    var startInfo = new ProcessStartInfo {
                        FileName = "uv",
                        Arguments = $"tool run --offline pymusiclooper {args}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };
                    if (!string.IsNullOrEmpty(CustomCachePath)) startInfo.EnvironmentVariables["UV_CACHE_DIR"] = CustomCachePath;
                    using (var process = new Process { StartInfo = startInfo }) {
                        process.Start();
                        output = await process.StandardOutput.ReadToEndAsync();
                        process.WaitForExit();
                    }
                } catch { }
            }

            return ParseLoopCandidates(output);
        }



        // --- 辅助方法已移除，因为不再支持自动下载 ---


        private void ParseAndReportProgress(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            // 特征值 1: Preparing packages... (10/41)
            var matchNum = System.Text.RegularExpressions.Regex.Match(data, @"\((\d+)/(\d+)\)");
            if (matchNum.Success)
            {
                string current = matchNum.Groups[1].Value;
                string total = matchNum.Groups[2].Value;
                OnStatusMessage?.Invoke($"[uv] Loading... ({current}/{total})");
                return;
            }

            // 特征值 2: 具体的 MiB 下载进度 (例如: " 5.00 MiB/7.65 MiB")
            var matchSize = System.Text.RegularExpressions.Regex.Match(data, @"(\d+\.?\d*\s*\w+iB/\d+\.?\d*\s*\w+iB)");
            if (matchSize.Success)
            {
                OnStatusMessage?.Invoke($"[uv] Downloading: {matchSize.Groups[1].Value}");
                return;
            }

            // 特征值 3: 解析进度百分比 (如果有的话)
            var matchPercent = System.Text.RegularExpressions.Regex.Match(data, @"(\d+)%");
            if (matchPercent.Success)
            {
                OnStatusMessage?.Invoke($"[uv] Progress: {matchPercent.Groups[1].Value}%");
                return;
            }
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
