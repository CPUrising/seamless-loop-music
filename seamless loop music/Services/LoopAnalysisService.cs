using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 循环分析服务
    /// 负责所有与循环点计算、PyMusicLooper 交互、候选结果缓存处理相关的逻辑
    /// </summary>
    public class LoopAnalysisService : ILoopAnalysisService
    {
        public event Action<string> OnStatusMessage;
        private readonly PyMusicLooperWrapper _pyMusicLooperWrapper;

        public LoopAnalysisService()
        {
            _pyMusicLooperWrapper = new PyMusicLooperWrapper();
            _pyMusicLooperWrapper.OnStatusMessage += msg => OnStatusMessage?.Invoke(msg);
        }

        public void SetCustomCachePath(string path)
        {
            _pyMusicLooperWrapper.CustomCachePath = path;
        }

        public void SetPyMusicLooperExecutablePath(string path)
        {
            _pyMusicLooperWrapper.PyMusicLooperExecutablePath = path;
        }

        public async Task<int> CheckEnvironmentAsync()
        {
            return await _pyMusicLooperWrapper.CheckEnvironmentAsync();
        }

        // --- JSON Helpers (�?PlayerService.cs 内置无依赖版) ---

        public string SerializeLoopCandidates(List<LoopCandidate> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                sb.Append("{");
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, 
                    "\"LoopStart\":{0},\"LoopEnd\":{1},\"Score\":{2},\"NoteDifference\":{3}", 
                    c.LoopStart, c.LoopEnd, c.Score, c.NoteDifference);
                sb.Append("}");
                if (i < list.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        public List<LoopCandidate> DeserializeLoopCandidates(string json)
        {
            var list = new List<LoopCandidate>();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                // Simple parser for [{"Key":Val,...},...]
                if (!json.Trim().StartsWith("[")) return list;
                
                // Split by "}," to get objects roughly
                var rawItems = json.Trim().Trim('[', ']').Split(new string[] { "}," }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in rawItems)
                {
                    var clean = item.Replace("{", "").Replace("}", "");
                    var candidate = new LoopCandidate();
                    
                    var props = clean.Split(',');
                    foreach (var p in props)
                    {
                        var kv = p.Split(':');
                        if (kv.Length < 2) continue;
                        var key = kv[0].Trim().Trim('"');
                        var val = kv[1].Trim();
                        
                        if (key == "LoopStart" && long.TryParse(val, out long ls)) candidate.LoopStart = ls;
                        if (key == "LoopEnd" && long.TryParse(val, out long le)) candidate.LoopEnd = le;
                        if (key == "Score" && double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sc)) candidate.Score = sc;
                        if (key == "NoteDifference" && double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double nd)) candidate.NoteDifference = nd;
                    }
                    if (candidate.LoopEnd > 0) list.Add(candidate);
                }
            }
            catch {}
            return list;
        }

        // --- 核心分析方法 ---

        /// <summary>
        /// 使用 PyMusicLooper 进行单次分析 (返回最佳点)
        /// </summary>
        public async Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath)
        {
            return await _pyMusicLooperWrapper.FindBestLoopAsync(filePath);
        }

        /// <summary>
        /// 获取 TOP 候选列�?(直接�?PyMusicLooper 获取，不涉及数据库缓存逻辑，那�?PlayerService 的事)
        /// </summary>
        public async Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath)
        {
            return await _pyMusicLooperWrapper.GetTopLoopPointsAsync(filePath);
        }
    }
}

