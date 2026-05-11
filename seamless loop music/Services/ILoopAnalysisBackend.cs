using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface ILoopAnalysisBackend
    {
        event Action<string> OnStatusMessage;

        Task<int> CheckEnvironmentAsync();

        Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath);
        Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath);
    }
}
