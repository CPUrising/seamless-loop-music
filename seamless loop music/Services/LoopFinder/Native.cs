using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services.LoopFinder
{
    public class Native
    {
        public event Action<string> OnStatusMessage;

        private static readonly bool DllPresent;
        public static readonly string LastError;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        static Native()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(typeof(Native).Assembly.Location) ?? ".";
                string dllPath = Path.Combine(dllDir, "loopfinder.dll");

                IntPtr hModule = LoadLibrary(dllPath);
                if (hModule == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    DllPresent = false;
                    LastError = $"DLL not found. Path: {dllPath} (Win32 error {err})";
                    return;
                }

                IntPtr pAnalyze = GetProcAddress(hModule, "lf_analyze_file");
                IntPtr pError   = GetProcAddress(hModule, "lf_get_last_error");

                FreeLibrary(hModule);

                if (pAnalyze == IntPtr.Zero || pError == IntPtr.Zero)
                {
                    DllPresent = false;
                    LastError = "DLL exports not found. Rebuild with `cmake --build build --config Release`";
                }
                else
                {
                    DllPresent = true;
                    LastError = null;
                }
            }
            catch (Exception ex)
            {
                DllPresent = false;
                LastError = $"Probe failed: {ex.Message}";
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LfLoopPoint
        {
            public long loopStart;
            public long loopEnd;
            public float noteDiff;
            public float loudnessDiff;
            public float score;
        }

        [DllImport("loopfinder.dll", CallingConvention = CallingConvention.Cdecl,
                   ExactSpelling = true)]
        private static extern int lf_analyze_file(
            [MarshalAs(UnmanagedType.LPStr)] string filepath,
            int topN,
            [Out] LfLoopPoint[] outPoints,
            int capacity);

        [DllImport("loopfinder.dll", CallingConvention = CallingConvention.Cdecl,
                   ExactSpelling = true)]
        private static extern IntPtr lf_get_last_error();

        public Task<int> CheckEnvironmentAsync()
        {
            if (DllPresent)
            {
                return Task.FromResult(0);
            }
            if (!string.IsNullOrEmpty(LastError))
            {
                OnStatusMessage?.Invoke($"[loopfinder] {LastError}");
            }
            return Task.FromResult(1);
        }

        public async Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath)
        {
            if (!DllPresent) return null;
            var candidates = await AnalyzeAsync(filePath, 1);
            if (candidates.Count > 0)
            {
                var best = candidates[0];
                return (best.LoopStart, best.LoopEnd, best.Score);
            }
            return null;
        }

        public async Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath)
        {
            if (!DllPresent) return new List<LoopCandidate>();
            return await AnalyzeAsync(filePath, 10);
        }

        [HandleProcessCorruptedStateExceptions]
        private async Task<List<LoopCandidate>> AnalyzeAsync(string filePath, int topN)
        {
            if (!File.Exists(filePath))
                return new List<LoopCandidate>();

            return await Task.Run(() => AnalyzeNative(filePath, topN));
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private List<LoopCandidate> AnalyzeNative(string filePath, int topN)
        {
            var results = new List<LoopCandidate>();
            try
            {
                OnStatusMessage?.Invoke("LOC:StatusAnalyzing");

                var capacity = Math.Max(topN, 10);
                var buffer = new LfLoopPoint[capacity];

                int count = lf_analyze_file(filePath, topN, buffer, buffer.Length);

                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        results.Add(new LoopCandidate
                        {
                            LoopStart = buffer[i].loopStart,
                            LoopEnd = buffer[i].loopEnd,
                            Score = buffer[i].score,
                            NoteDifference = buffer[i].noteDiff,
                            LoudnessDifference = buffer[i].loudnessDiff
                        });
                    }
                }
                else if (count < 0)
                {
                    var errorPtr = lf_get_last_error();
                    var errorMsg = errorPtr != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(errorPtr)
                        : "unknown error";
                    OnStatusMessage?.Invoke($"[loopfinder] {errorMsg}");
                }
            }
            catch (AccessViolationException ex)
            {
                OnStatusMessage?.Invoke($"[loopfinder] native crash: {ex.Message}");
            }
            catch (SEHException ex)
            {
                OnStatusMessage?.Invoke($"[loopfinder] native error 0x{ex.ErrorCode:X8}");
            }
            catch (DllNotFoundException)
            {
                OnStatusMessage?.Invoke("[loopfinder] DLL not found");
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[loopfinder] {ex.Message}");
            }
            return results;
        }
    }
}
