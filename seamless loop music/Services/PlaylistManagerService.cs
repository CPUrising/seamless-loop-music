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
    public class PlaylistManagerService : IPlaylistManagerService
    {
        private readonly IDatabaseHelper _dbHelper;
        private readonly TrackMetadataService _metadataService;
        public Action<string> OnStatusMessage { get; set; }

        public PlaylistManagerService(IDatabaseHelper dbHelper, TrackMetadataService metadataService)
        {
            _dbHelper = dbHelper;
            _metadataService = metadataService;
        }

        public List<Playlist> GetAllPlaylists()
        {
            return _dbHelper.GetAllPlaylists();
        }

        public int CreatePlaylist(string name)
        {
            return _dbHelper.AddPlaylist(name);
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
                 // 如果这首歌还没进资源库，先存一下拿ID
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
                            DisplayName = null,
                            LastModified = DateTime.Now
                        };
                        _metadataService.FillMetadata(track);
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

                string[] aSuffixes = ["_A", "_a", "_intro", "_Intro"];
                string[] bSuffixes = ["_B", "_b", "_loop", "_Loop"];

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

        public async Task<int> CleanupMissingTracksAsync()
        {
            return await Task.Run(() =>
            {
                OnStatusMessage?.Invoke("Scanning for missing files in library...");
                int deleted = _dbHelper.CleanupMissingFiles();
                if (deleted > 0)
                {
                    OnStatusMessage?.Invoke($"Cleanup complete. Removed {deleted} missing tracks from database.");
                }
                else
                {
                    OnStatusMessage?.Invoke("Cleanup complete. No missing files found.");
                }
                return deleted;
            });
        }
    }
}
