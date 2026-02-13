using System;
using System.IO;
using System.Linq;
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
        private readonly DatabaseHelper _dbHelper;

        public TrackMetadataService(DatabaseHelper dbHelper)
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
            // 1. 去数据库查户口 (指纹匹配)
            var dbTrack = _dbHelper.GetTrack(filePath, totalSamples);
            
            // 模糊匹配：如果采样数变了，尝试通过文件名找回
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
                // 纯新人
                currentTrack.LoopStart = isABMode ? defaultLoopStart : 0;
                currentTrack.LoopEnd = totalSamples;
                _dbHelper.SaveTrack(currentTrack);
            }

            return currentTrack;
        }

        public void SaveTrack(MusicTrack track)
        {
            if (track != null) _dbHelper.SaveTrack(track);
        }
    }
}
