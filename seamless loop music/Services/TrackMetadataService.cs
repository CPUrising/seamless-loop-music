using System;
using System.IO;
using System.Linq;
using Dapper;
using seamless_loop_music.Data;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 曲目元数据服务
    /// 负责曲目属性的处理、数据库匹配、A/B 段探测以及持久化
    /// </summary>
    public class TrackMetadataService
    {
        private readonly IDatabaseHelper _dbHelper;

        public TrackMetadataService(IDatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public string FindPartB(string filePath)
        {
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

        /// <summary>
        /// 根据加载的音频信息，从数据库获取或创建曲目元数据
        /// </summary>
        public MusicTrack GetOrUpdateTrackMetadata(string filePath, long totalSamples, long defaultLoopStart, bool isABMode)
        {
            // 1. 去数据库查询（指纹匹配）
            var dbTrack = _dbHelper.GetTrack(filePath, totalSamples);
            
            // 模糊匹配：如果采样数变了，尝试通过文件名找
            if (dbTrack == null)
            {
                dbTrack = _dbHelper.GetAllTracks().FirstOrDefault(t => 
                    Path.GetFileName(t.FilePath).Equals(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));
            }

            var currentTrack = new MusicTrack 
            { 
                FilePath = filePath, 
                FileName = Path.GetFileName(filePath),
                TotalSamples = totalSamples 
            };

            // 填充物理文件元数据 (TagLib)
            FillMetadataFromFile(currentTrack);

            if (dbTrack != null)
            {
                currentTrack.Id = dbTrack.Id;
                currentTrack.DisplayName = dbTrack.DisplayName;
                currentTrack.LoopCandidatesJson = dbTrack.LoopCandidatesJson;
                
                // A/B 自动修正
                if (isABMode && (dbTrack.LoopStart == 0 || Math.Abs(dbTrack.TotalSamples - totalSamples) > 1000))
                {
                    currentTrack.LoopStart = defaultLoopStart;
                    currentTrack.LoopEnd = totalSamples;
                    _dbHelper.SaveTrack(currentTrack);
                }
                else
                {
                    currentTrack.LoopStart = dbTrack.LoopStart;
                    currentTrack.LoopEnd = (dbTrack.LoopEnd <= 0) ? totalSamples : dbTrack.LoopEnd;
                }
            }
            else
            {
                // 纯新增
                currentTrack.LoopStart = isABMode ? defaultLoopStart : 0;
                currentTrack.LoopEnd = totalSamples;
                _dbHelper.SaveTrack(currentTrack);
            }

            return currentTrack;
        }

        private void FillMetadataFromFile(MusicTrack track)
        {
            try
            {
                if (!File.Exists(track.FilePath)) return;

                using (var file = TagLib.File.Create(track.FilePath))
                {
                    if (file.Tag != null)
                    {
                        track.Artist = file.Tag.FirstPerformer;
                        track.Album = file.Tag.Album;
                        track.AlbumArtist = file.Tag.FirstAlbumArtist;

                        // 如果之前没有设置过显示名称，尝试用标题
                        if (string.IsNullOrEmpty(track.DisplayName) && !string.IsNullOrEmpty(file.Tag.Title))
                        {
                            track.DisplayName = file.Tag.Title;
                        }

                        // 获取封面 (带缓存)
                        track.CoverPath = GetOrExtractCover(track);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TagLib Read Error: " + ex.Message);
            }
        }

        public void SaveTrack(MusicTrack track)
        {
            if (track != null) _dbHelper.SaveTrack(track);
        }

        public string GetOrExtractCover(MusicTrack track)
        {
            try
            {
                if (track == null || string.IsNullOrEmpty(track.FilePath)) return null;

                // 1. 如果数据库已经记录了路径，且文件存在，直接返回
                if (!string.IsNullOrEmpty(track.CoverPath) && System.IO.File.Exists(track.CoverPath))
                {
                    return track.CoverPath;
                }

                // 2. 尝试寻找同专辑的其他曲目是否已经缓存了封面
                string albumKey = !string.IsNullOrEmpty(track.Album) ? track.Album : "UnknownAlbum";
                string artistKey = !string.IsNullOrEmpty(track.Artist) ? track.Artist : "UnknownArtist";
                
                // 这种查询比较重，我们可以通过数据库助手找
                using (var db = _dbHelper.GetConnection())
                {
                    string existingPath = db.QueryFirstOrDefault<string>(
                        "SELECT CoverPath FROM LoopPoints WHERE Album = @Album AND CoverPath IS NOT NULL AND CoverPath != '' LIMIT 1",
                        new { Album = track.Album });

                    if (!string.IsNullOrEmpty(existingPath) && System.IO.File.Exists(existingPath))
                    {
                        track.CoverPath = existingPath;
                        return existingPath;
                    }
                }

                // 3. 准备缓存目录
                string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "Covers");
                if (!System.IO.Directory.Exists(cacheDir)) System.IO.Directory.CreateDirectory(cacheDir);

                // 4. 尝试从文件提取 (TagLib)
                using (var file = TagLib.File.Create(track.FilePath))
                {
                    if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                    {
                        var pic = file.Tag.Pictures[0];
                        
                        // 仅使用专辑名计算指纹，符合 CPU 大人的严格要求
                        string albumId = !string.IsNullOrEmpty(track.Album) ? track.Album : Path.GetFileName(Path.GetDirectoryName(track.FilePath));
                        string safeId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(albumId ?? "Unknown"))
                                        .Replace("/", "_").Replace("+", "-").Replace("=", "");
                        
                        if (safeId.Length > 50) safeId = safeId.Substring(0, 50); // 防止路径过长

                        string cachePath = Path.Combine(cacheDir, $"album-{safeId}.jpg");
                        
                        // 如果文件已经存在，就没必要再写一次了
                        if (!System.IO.File.Exists(cachePath))
                        {
                            System.IO.File.WriteAllBytes(cachePath, pic.Data.Data);
                        }
                        
                        track.CoverPath = cachePath;
                        return cachePath;
                    }
                }

                // 5. 尝试从本地文件夹匹配常见封面名
                string trackDir = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrEmpty(trackDir))
                {
                    string[] coverNames = { "cover.jpg", "folder.jpg", "album.jpg", "front.jpg", "cover.png" };
                    foreach (var name in coverNames)
                    {
                        string p = Path.Combine(trackDir, name);
                        if (System.IO.File.Exists(p))
                        {
                            track.CoverPath = p;
                            return p;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Cover Extract Error: " + ex.Message);
            }
            return null;
        }
    }
}
