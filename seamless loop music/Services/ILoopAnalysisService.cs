using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 循环点分析服务接口
    /// 后端使用原生 loopfinder.dll (LoopFinder.Native)
    /// </summary>
    public interface ILoopAnalysisService
    {
        event Action<string> OnStatusMessage;

        string LastError { get; }

        Task<int> CheckEnvironmentAsync();

        Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath);
        Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath);

        string SerializeLoopCandidates(List<LoopCandidate> list);
        List<LoopCandidate> DeserializeLoopCandidates(string json);
    }
}
