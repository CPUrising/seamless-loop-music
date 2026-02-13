using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Vorbis;
using seamless_loop_music.Data;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 歌单管理服务
    /// 负责所有与歌单增删改查、文件夹扫描、文件入库相关的逻辑
    /// </summary>
    public class PlaylistManagerService
    {
        private readonly DatabaseHelper _dbHelper;
        
        // 外部注入通知回调
        public Action<string> OnStatusMessage;

        public PlaylistManagerService(DatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public List<PlaylistFolder> GetAllPlaylists()
        {
            return _dbHelper.GetAllPlaylists().ToList();
        }

        public int CreatePlaylist(string name, string folderPath = null, bool isLinked = false)
        {
            return _dbHelper.AddPlaylist(name, folderPath, isLinked);
        }

        public void RenamePlaylist(int playlistId, string newName)
        {
            _dbHelper.RenamePlaylist(playlistId, newName);
        }

        public void DeletePlaylist(int playlistId)
        {
            _dbHelper.DeletePlaylist(playlistId);
        }

        public void AddTrackToPlaylist(int playlistId, MusicTrack track)
        {
            if (track.Id <= 0)
            {
                 // 如果这首歌还没进资源库，先存一下拿到 ID
                 _dbHelper.SaveTrack(track);
            }
            _dbHelper.AddSongToPlaylist(playlistId, track.Id);
        }

        public void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks)
        {
             // 批量入库新歌（如果没入库的话）
             var newTracks = tracks.Where(t => t.Id <= 0).ToList();
             if (newTracks.Any())
             {
                 _dbHelper.BulkSaveTracksAndAddToPlaylist(newTracks, playlistId);
             }

             // 关联已存在的歌曲
             var existingTracks = tracks.Where(t => t.Id > 0).ToList();
             foreach (var t in existingTracks)
             {
                 _dbHelper.AddSongToPlaylist(playlistId, t.Id);
             }
        }

        public void RemoveTrackFromPlaylist(int playlistId, int songId)
        {
            if (songId > 0)
            {
                _dbHelper.RemoveSongFromPlaylist(playlistId, songId);
            }
        }
        
        /// <summary>
        /// 添加多个文件到手动歌单（手动模式）
        /// </summary>
        public async Task AddFilesToPlaylistAsync(int playlistId, string[] filePaths)
        {
             await Task.Run(() => {
                var tracksToAdd = new List<MusicTrack>();
                
                foreach (var f in filePaths)
                {
                    try 
                    {
                        if (!File.Exists(f)) continue;
                        
                        // 1. 获取基础采样数
                        long samples = GetTotalSamples(f);
                        if (samples <= 0) continue;

                        var track = new MusicTrack 
                        { 
                            FilePath = f, 
                            FileName = Path.GetFileName(f), 
                            TotalSamples = samples,
                            LoopEnd = samples,
                            LoopStart = 0,
                            DisplayName = Path.GetFileNameWithoutExtension(f),
                            LastModified = DateTime.Now
                        };
                        tracksToAdd.Add(track);
                    }
                    catch { /* 忽略损坏文件 */ }
                }

                if (tracksToAdd.Count > 0)
                {
                    _dbHelper.BulkSaveTracksAndAddToPlaylist(tracksToAdd, playlistId);
                }
             });
        }


        public async Task AddFolderToPlaylistAsync(int playlistId, string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            // 1. 记录文件夹
            _dbHelper.AddPlaylistFolder(playlistId, folderPath);
            
            // 2. 刷新歌单内容
            await RefreshPlaylistAsync(playlistId);
        }

        public async Task RemoveFolderFromPlaylistAsync(int playlistId, string folderPath)
        {
            _dbHelper.RemovePlaylistFolder(playlistId, folderPath);
            await RefreshPlaylistAsync(playlistId);
        }

        public List<string> GetPlaylistFolders(int playlistId)
        {
            return _dbHelper.GetPlaylistFolders(playlistId);
        }

        /// <summary>
        /// 刷新歌单内容：根据记录的文件夹重新扫描，添加新歌，移除消失的歌曲
        /// </summary>
        public async Task RefreshPlaylistAsync(int playlistId)
        {
            await Task.Run(() => {
                // 1. 获取所有关联文件夹
                var folders = _dbHelper.GetPlaylistFolders(playlistId);
                if (folders == null || folders.Count == 0) return;

                OnStatusMessage?.Invoke($"Refreshing playlist {playlistId} from {folders.Count} folders...");

                // 2. 扫描所有文件夹下的文件
                var disksFilesMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); 

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;

                    var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(s => s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                                    s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                    s.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase));
                    
                    foreach (var f in files)
                    {
                        if (!disksFilesMap.ContainsKey(f))
                        {
                            disksFilesMap[f] = 0; 
                        }
                    }
                }

                // 3. 获取当前歌单内的歌曲
                var currentTracks = _dbHelper.GetPlaylistTracks(playlistId).ToList();

                // 4. 找出候选新增（或需要更新的）文件
                var allDiskFiles = disksFilesMap.Keys.ToList();
                
                // --- 过滤 B 文件 ---
                var aPartFiles = new List<string>();
                var bFilesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var f in allDiskFiles)
                {
                    string dir = Path.GetDirectoryName(f);
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    string ext = Path.GetExtension(f);
                    string[] bSuffixes = { "_B", "_b", "_loop", "_Loop" };
                    string[] aSuffixes = { "_A", "_a", "_intro", "_Intro" };

                    for (int i = 0; i < bSuffixes.Length; i++)
                    {
                        if (fileName.EndsWith(bSuffixes[i]))
                        {
                            string baseName = fileName.Substring(0, fileName.Length - bSuffixes[i].Length);
                            string aName = baseName + aSuffixes[i];
                            string aPath = Path.Combine(dir, aName + ext);
                            
                            // 如果对应的 A 文件存在，那么这个 B 就不作为独立曲目入库
                            // 注意：这里需要检查 disksFilesMap 是否包含 aPath
                            // 有可能 aPath 也在当前的 allDiskFiles 里
                            if (disksFilesMap.ContainsKey(aPath))
                            {
                                bFilesToIgnore.Add(f);
                                break;
                            }
                        }
                    }
                }

                foreach (var f in allDiskFiles)
                {
                    if (!bFilesToIgnore.Contains(f)) aPartFiles.Add(f);
                }

                // 5. 准备移除列表
                var tracksToRemove = new List<int>();
                foreach (var track in currentTracks)
                {
                    if (!disksFilesMap.ContainsKey(track.FilePath))
                    {
                        tracksToRemove.Add(track.Id);
                    }
                    else 
                    {
                        string f = track.FilePath;
                        if (bFilesToIgnore.Contains(f))
                        {
                            tracksToRemove.Add(track.Id);
                        }
                    }
                }

                // --- 执行数据库更新 ---
                // A. 核心：分析并添加/更新曲目
                if (aPartFiles.Count > 0)
                {
                    OnStatusMessage?.Invoke($"Analyzing {aPartFiles.Count} tracks for changes...");
                    var tracksToSave = new List<MusicTrack>();
                    int processed = 0;

                    foreach (var f in aPartFiles)
                    {
                        try 
                        {
                            long samplesA = GetTotalSamples(f);
                            if (samplesA <= 0) continue;

                            long totalSamples = samplesA;
                            long loopStart = 0;

                            string partB = FindPartB(f);
                            if (!string.IsNullOrEmpty(partB))
                            {
                                long samplesB = GetTotalSamples(partB);
                                if (samplesB > 0)
                                {
                                    totalSamples += samplesB;
                                    loopStart = samplesA; // A/B 模式默认起点
                                }
                            }

                            var existingTrack = currentTracks.FirstOrDefault(t => t.FilePath.Equals(f, StringComparison.OrdinalIgnoreCase));
                            
                            if (existingTrack == null || existingTrack.TotalSamples != totalSamples)
                            {
                                tracksToSave.Add(new MusicTrack 
                                { 
                                    Id = existingTrack?.Id ?? 0, 
                                    FilePath = f, 
                                    FileName = Path.GetFileName(f), 
                                    TotalSamples = totalSamples,
                                    LoopEnd = totalSamples,
                                    LoopStart = loopStart,
                                    DisplayName = existingTrack?.DisplayName ?? Path.GetFileNameWithoutExtension(f),
                                    LastModified = DateTime.Now
                                });
                            }
                        } 
                        catch { }
                        
                        processed++;
                        if (processed % 10 == 0) OnStatusMessage?.Invoke($"Analyzing... ({processed}/{aPartFiles.Count})");
                    }

                    if (tracksToSave.Count > 0)
                    {
                        _dbHelper.BulkSaveTracksAndAddToPlaylist(tracksToSave, playlistId);
                    }
                }

                // B. 移除消失的歌
                if (tracksToRemove.Count > 0)
                {
                    foreach (var songId in tracksToRemove)
                    {
                        _dbHelper.RemoveSongFromPlaylist(playlistId, songId);
                    }
                }

                OnStatusMessage?.Invoke($"Refresh complete. Analyzed {aPartFiles.Count} files, removed {tracksToRemove.Count} stale records.");
            });
        }
        
        public List<MusicTrack> LoadPlaylistFromDb(int playlistId)
        {
            return _dbHelper.GetPlaylistTracks(playlistId).ToList();
        }

        public MusicTrack GetStoredTrackInfo(string filePath)
        {
            return _dbHelper.GetTrack(filePath, 0);
        }

        public void UpdateOfflineTrack(MusicTrack track)
        {
            if (track == null || track.TotalSamples <= 0) return;
            try {
                _dbHelper.SaveTrack(track);
            } catch (Exception ex) {
                OnStatusMessage?.Invoke($"Offline Save Error: {ex.Message}");
            }
        }
        
        // --- Helpers: 这部分需要复制过来，因为它们是文件读取的基础 ---

        public long GetTotalSamples(string filePath)
        {
            try {
                using (var reader = CreateReader(filePath)) {
                    if (reader == null) return 0;
                    return reader.Length / reader.WaveFormat.BlockAlign; 
                }
            } catch { return 0; }
        }

        private WaveStream CreateReader(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            switch (ext)
            {
                case ".wav": return new WaveFileReader(filePath);
                case ".ogg": return new VorbisWaveReader(filePath);
                case ".mp3": return new Mp3FileReader(filePath);
                default: 
                    try { return new AudioFileReader(filePath); } catch { return null; }
            }
        }

        private string FindPartB(string filePath)
        {
            // 这是一个简单的帮助方法，和 PlayerService 里的一样
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);

                string[] aSuffixes = { "_A", "_a", "_intro", "_Intro" };
                string[] bSuffixes = { "_B", "_b", "_loop", "_Loop" };

                for (int i = 0; i < aSuffixes.Length; i++)
                {
                    if (fileName.EndsWith(aSuffixes[i]))
                    {
                        string baseName = fileName.Substring(0, fileName.Length - aSuffixes[i].Length);
                        string bName = baseName + bSuffixes[i];
                        string bPath = Path.Combine(dir, bName + ext);
                        if (File.Exists(bPath)) return bPath;
                    }
                }
            }
            catch { }
            return null;
        }

        public void UpdatePlaylistsSortOrder(List<int> playlistIds) => _dbHelper.UpdatePlaylistsSortOrder(playlistIds);

        public void UpdateTracksSortOrder(int playlistId, List<int> songIds) => _dbHelper.UpdateTracksSortOrder(playlistId, songIds);
    }
}
