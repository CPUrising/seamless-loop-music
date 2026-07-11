using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public class PlaybackStatisticsOutbox
    {
        private readonly string _path;
        private readonly string _backupPath;
        private readonly object _fileLock = new object();

        public PlaybackStatisticsOutbox() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PlaybackStatistics.pending.json")) { }

        public PlaybackStatisticsOutbox(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An outbox path is required.", nameof(path));
            _path = path;
            _backupPath = path + ".bak";
        }

        public PlaybackStatisticsOutboxState LoadState()
        {
            lock (_fileLock)
            {
                PlaybackStatisticsOutboxState primary;
                if (TryRead(_path, out primary))
                {
                    TryDelete(_backupPath);
                    return primary;
                }

                PlaybackStatisticsOutboxState backup;
                if (TryRead(_backupPath, out backup))
                {
                    RestoreBackup();
                    return backup;
                }

                if (File.Exists(_path)) IsolateCorrupt(_path);
                if (File.Exists(_backupPath)) IsolateCorrupt(_backupPath);
                var empty = new PlaybackStatisticsOutboxState();
                SaveEnvelopeCore(empty);
                return empty;
            }
        }

        public Task SaveSettlementEventsAsync(IEnumerable<PlaybackStatisticsSettlement> settlements)
        {
            return Task.Run(() => SaveSettlementEvents(settlements));
        }

        public void SaveSettlementEvents(IEnumerable<PlaybackStatisticsSettlement> settlements)
        {
            if (settlements == null) throw new ArgumentNullException(nameof(settlements));
            SaveEnvelopeCore(new PlaybackStatisticsOutboxState { SettlementEvents = settlements.ToList() });
        }

        private void SaveJsonCore(string json)
        {
            lock (_fileLock)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    WriteTemp(tempPath, json);
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

        private bool TryRead(string path, out PlaybackStatisticsOutboxState state)
        {
            state = null;
            if (!File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                var root = JObject.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                var allowed = new HashSet<string>(StringComparer.Ordinal) { "Version", "SettlementEvents" };
                if (root.Properties().Any(property => !allowed.Contains(property.Name))) return false;
                var envelope = root.ToObject<PlaybackStatisticsOutboxState>();
                if (envelope == null || root["Version"] == null || root["SettlementEvents"] == null || envelope.Version != 2 || envelope.SettlementEvents == null || envelope.SettlementEvents.Any(x => !IsValid(x))) return false;
                state = envelope;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Playback statistics] Invalid outbox file {path}: {ex.Message}");
                return false;
            }
        }

        private void SaveEnvelopeCore(PlaybackStatisticsOutboxState state)
        {
            if (state == null || state.Version != 2 || state.SettlementEvents == null || state.SettlementEvents.Any(x => !IsValid(x))) throw new ArgumentException("Invalid settlement outbox state.");
            SaveJsonCore(JsonConvert.SerializeObject(state, Formatting.None));
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

        private static bool IsValid(PlaybackStatisticsSettlement settlement)
        {
            try { settlement?.Validate(); return settlement != null; } catch { return false; }
        }
    }
}
