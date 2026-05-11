using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Prism.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Events;

namespace seamless_loop_music.Services
{
    public class LoopAnalysisService : ILoopAnalysisService
    {
        public event Action<string> OnStatusMessage;
        private readonly ILoopAnalysisBackend _backend;
        private readonly IEventAggregator _eventAggregator;

        public LoopAnalysisService(ILoopAnalysisBackend backend, IEventAggregator eventAggregator)
        {
            _backend = backend;
            _eventAggregator = eventAggregator;
            _backend.OnStatusMessage += msg =>
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                {
                    OnStatusMessage?.Invoke(msg);
                    _eventAggregator.GetEvent<StatusMessageEvent>().Publish(msg);
                }));
            };
        }

        public void SetCustomCachePath(string path)
        {
            if (_backend is PyMusicLooperWrapper py)
                py.CustomCachePath = path;
        }

        public void SetPyMusicLooperExecutablePath(string path)
        {
            if (_backend is PyMusicLooperWrapper py)
                py.PyMusicLooperExecutablePath = path;
        }

        public async Task<int> CheckEnvironmentAsync()
        {
            return await _backend.CheckEnvironmentAsync();
        }

        public string SerializeLoopCandidates(List<LoopCandidate> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                sb.Append("{");
                sb.AppendFormat(CultureInfo.InvariantCulture,
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
                if (!json.Trim().StartsWith("[")) return list;

                var rawItems = json.Trim().Trim('[', ']').Split(
                    new string[] { "}," }, StringSplitOptions.RemoveEmptyEntries);
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

                        if (key == "LoopStart" && long.TryParse(val, out long ls))
                            candidate.LoopStart = ls;
                        if (key == "LoopEnd" && long.TryParse(val, out long le))
                            candidate.LoopEnd = le;
                        if (key == "Score" && double.TryParse(val,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture, out double sc))
                            candidate.Score = sc;
                        if (key == "NoteDifference" && double.TryParse(val,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture, out double nd))
                            candidate.NoteDifference = nd;
                    }
                    if (candidate.LoopEnd > 0) list.Add(candidate);
                }
            }
            catch { }
            return list;
        }

        public async Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath)
        {
            return await _backend.FindBestLoopAsync(filePath);
        }

        public async Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath)
        {
            return await _backend.FetchTopLoopCandidatesAsync(filePath);
        }
    }
}
