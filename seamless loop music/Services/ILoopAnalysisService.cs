using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface ILoopAnalysisService
    {
        event Action<string> OnStatusMessage;
        
        Task<int> CheckEnvironmentAsync();
        void SetCustomCachePath(string path);
        void SetPyMusicLooperExecutablePath(string path);
        
        Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath);
        Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath);
        
        string SerializeLoopCandidates(List<LoopCandidate> list);
        List<LoopCandidate> DeserializeLoopCandidates(string json);
    }
}
