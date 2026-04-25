using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            FillMetadata(currentTrack);

            if (dbTrack != null)
            {
                currentTrack.Id = dbTrack.Id;
                
                // 只有当本地标签没读到 DisplayName，且数据库里有值时，才用数据库的
                if (string.IsNullOrEmpty(currentTrack.DisplayName))
                {
                    currentTrack.DisplayName = dbTrack.DisplayName;
                }
                
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

        public void FillMetadata(MusicTrack track)
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
                    }
                }

                // 获取封面 (带缓存) - 移到 using 块外面，避免文件锁定冲突
                track.CoverPath = GetOrExtractCover(track);
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

                // 1. 如果数据库已经记录了路径，且文件有效（存在且不为0字节），直接返回
                if (IsFileValid(track.CoverPath))
                {
                    return track.CoverPath;
                }

                // 2. 尝试寻找同专辑的其他曲目是否已经缓存了封面
                if (string.IsNullOrEmpty(track.Album)) goto skip_album_sharing;

                using (var db = _dbHelper.GetConnection())
                {
                    // 严格按照 CPU 大人要求：仅通过专辑名匹配，实现一辑一封
                    string existingPath = db.QueryFirstOrDefault<string>(@"
                        SELECT CoverPath 
                        FROM Albums 
                        WHERE Name = @Album 
                          AND CoverPath IS NOT NULL 
                          AND CoverPath != '' 
                        LIMIT 1",
                        new { Album = track.Album });

                    if (IsFileValid(existingPath))
                    {
                        track.CoverPath = existingPath;
                        return existingPath;
                    }
                }

                skip_album_sharing:
                // 3. 准备缓存目录
                string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "Covers");
                if (!System.IO.Directory.Exists(cacheDir)) System.IO.Directory.CreateDirectory(cacheDir);

                // 4. 尝试从文件提取 (TagLib)
                using (var file = TagLib.File.Create(track.FilePath))
                {
                    if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                    {
                        var pic = file.Tag.Pictures[0];
                        
                        // 生成基于专辑信息的确定性 GUID，确保同专辑共享同一个物理文件
                        // 逻辑：严格按照 CPU 大人要求，仅使用专辑名（无专辑名则用文件夹名）作为指纹
                        string folderName = Path.GetFileName(Path.GetDirectoryName(track.FilePath) ?? "UnknownFolder");
                        string albumKey = track.Album ?? folderName;
                        
                        string guidKey;
                        using (var md5 = MD5.Create())
                        {
                            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(albumKey));
                            guidKey = new Guid(hash).ToString();
                        }
                        
                        string cachePath = Path.Combine(cacheDir, $"{guidKey}.jpg");
                        
                        // 如果物理文件不存在或已损坏（0字节），则写入新提取的数据
                        if (!IsFileValid(cachePath))
                        {
                            if (pic.Data.Data != null && pic.Data.Data.Length > 0)
                            {
                                SaveCompressedImage(pic.Data.Data, cachePath);
                            }
                            else
                            {
                                // 如果提取到的原始数据就是空的，则跳过本次缓存设置
                                goto skip_to_local_folder;
                            }
                        }
                        
                        track.CoverPath = cachePath;
                        return cachePath;
                    }
                }

                skip_to_local_folder:

                // 5. 尝试从本地文件夹匹配常见封面名
                string trackDir = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrEmpty(trackDir))
                {
                    string[] coverNames = { "cover.jpg", "folder.jpg", "album.jpg", "front.jpg", "cover.png" };
                    foreach (var name in coverNames)
                    {
                        string p = Path.Combine(trackDir, name);
                        if (IsFileValid(p))
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

        /// <summary>
        /// 压缩并保存图片为 JPEG 格式
        /// </summary>
        /// <param name="rawData">原始图片字节数据</param>
        /// <param name="targetPath">保存路径</param>
        /// <param name="maxSide">最大边长</param>
        /// <param name="quality">压缩质量 (1-100)</param>
        private void SaveCompressedImage(byte[] rawData, string targetPath, int maxSide = 800, int quality = 80)
        {
            try
            {
                using (var ms = new MemoryStream(rawData))
                {
                    // 1. 解码图片
                    var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) return;

                    var frame = decoder.Frames[0];
                    BitmapSource source = frame;

                    // 2. 只有当图片长或宽超过最大值时才进行缩放
                    if (frame.PixelWidth > maxSide || frame.PixelHeight > maxSide)
                    {
                        double scale = (double)maxSide / Math.Max(frame.PixelWidth, frame.PixelHeight);
                        source = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    }

                    // 3. 使用 JPEG 编码器进行压缩
                    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(BitmapFrame.Create(source));

                    using (var fs = File.Create(targetPath))
                    {
                        encoder.Save(fs);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SaveCompressedImage Error: " + ex.Message);
                // 如果压缩失败，尝试原始写入作为保底
                try { File.WriteAllBytes(targetPath, rawData); } catch { }
            }
        }


        /// <summary>
        /// 检查文件是否有效（存在且大小大于0）
        /// </summary>
        private bool IsFileValid(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                var fi = new FileInfo(path);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
