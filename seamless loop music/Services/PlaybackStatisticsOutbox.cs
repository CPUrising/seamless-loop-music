using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public class PlaybackStatisticsOutbox
    {
        private readonly string _path;
        private readonly string _backupPath;
        private readonly object _fileLock = new object();

        public PlaybackStatisticsOutbox() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PlaybackSegments.pending.json")) { }

        public PlaybackStatisticsOutbox(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An outbox path is required.", nameof(path));
            _path = path;
            _backupPath = path + ".bak";
        }

        public IReadOnlyList<PlaybackSegment> Load()
        {
            lock (_fileLock)
            {
                List<PlaybackSegment> primary;
                if (TryRead(_path, out primary))
                {
                    TryDelete(_backupPath);
                    return primary;
                }

                List<PlaybackSegment> backup;
                if (TryRead(_backupPath, out backup))
                {
                    RestoreBackup();
                    return backup;
                }

                if (File.Exists(_path)) IsolateCorrupt(_path);
                if (File.Exists(_backupPath)) IsolateCorrupt(_backupPath);
                return new List<PlaybackSegment>();
            }
        }

        public Task SaveAsync(IEnumerable<PlaybackSegment> segments)
        {
            return Task.Run(() => SaveCore(segments));
        }

        private void SaveCore(IEnumerable<PlaybackSegment> segments)
        {
            lock (_fileLock)
            {
                var valid = (segments ?? Enumerable.Empty<PlaybackSegment>()).Where(IsValid).ToList();
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (valid.Count == 0)
                {
                    DeleteIfExists(_path);
                    DeleteIfExists(_backupPath);
                    CleanupTempFiles();
                    return;
                }

                var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    WriteTemp(tempPath, JsonConvert.SerializeObject(valid, Formatting.None));
                    if (!File.Exists(_path))
                    {
                        File.Move(tempPath, _path);
                    }
                    else
                    {
                        try
                        {
                            DeleteIfExists(_backupPath);
                            File.Replace(tempPath, _path, _backupPath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Playback statistics] Atomic outbox replace failed, using rename recovery: {ex.Message}");
                            DeleteIfExists(_backupPath);
                            File.Move(_path, _backupPath);
                            try
                            {
                                File.Move(tempPath, _path);
                            }
                            catch
                            {
                                try { if (!File.Exists(_path) && File.Exists(_backupPath)) File.Move(_backupPath, _path); }
                                catch (Exception restoreEx) { Debug.WriteLine($"[Playback statistics] Could not restore outbox backup: {restoreEx.Message}"); }
                                throw;
                            }
                        }
                        TryDelete(_backupPath);
                    }
                }
                catch
                {
                    DeleteIfExists(tempPath);
                    throw;
                }
            }
        }

        private bool TryRead(string path, out List<PlaybackSegment> segments)
        {
            segments = null;
            if (!File.Exists(path)) return false;
            try
            {
                var deserialized = JsonConvert.DeserializeObject<List<PlaybackSegment>>(File.ReadAllText(path)) ?? new List<PlaybackSegment>();
                if (deserialized.Any(segment => !IsValid(segment))) return false;

                segments = deserialized;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Playback statistics] Invalid outbox file {path}: {ex.Message}");
                return false;
            }
        }

        private void RestoreBackup()
        {
            try
            {
                if (File.Exists(_path)) IsolateCorrupt(_path);
                if (File.Exists(_backupPath)) File.Move(_backupPath, _path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Playback statistics] Could not restore outbox backup: {ex.Message}");
            }
        }

        private void IsolateCorrupt(string path)
        {
            try { File.Move(path, path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".corrupt"); }
            catch (Exception ex) { Debug.WriteLine($"[Playback statistics] Could not isolate corrupt outbox: {ex.Message}"); }
        }

        private static void WriteTemp(string path, string json)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private void CleanupTempFiles()
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            foreach (var tempPath in Directory.GetFiles(directory, Path.GetFileName(_path) + ".*.tmp")) DeleteIfExists(tempPath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void TryDelete(string path)
        {
            try { DeleteIfExists(path); }
            catch (Exception ex) { Debug.WriteLine($"[Playback statistics] Could not delete stale outbox file: {ex.Message}"); }
        }

        private static bool IsValid(PlaybackSegment segment)
        {
            return segment != null && !string.IsNullOrWhiteSpace(segment.SegmentId) && segment.TrackId > 0 &&
                segment.StartedAtUtcMs >= 0 && segment.DurationMs > 0 && segment.DurationMs <= long.MaxValue - segment.StartedAtUtcMs;
        }
    }
}
