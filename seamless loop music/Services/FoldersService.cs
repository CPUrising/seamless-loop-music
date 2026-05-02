using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using seamless_loop_music.Data;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public class FoldersService : IFoldersService
    {
        private readonly IDatabaseHelper _dbHelper;

        public FoldersService(IDatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<List<SubfolderItem>> GetRootFoldersAsync()
        {
            return await Task.Run(() =>
            {
                var folders = _dbHelper.GetMusicFolders();
                return folders.Select(f => new SubfolderItem 
                { 
                    Name = Path.GetFileName(f) ?? f, 
                    Path = f,
                    IsRoot = true 
                }).ToList();
            });
        }

        public async Task<List<SubfolderItem>> GetSubfoldersAsync(string parentPath)
        {
            if (string.IsNullOrEmpty(parentPath)) return new List<SubfolderItem>();

            return await Task.Run(() =>
            {
                // 从数据库获取所有属于该路径（或其子路径）的曲目
                // 我们通过分析 FilePath 来确定有哪些子文件夹
                var allTracks = _dbHelper.GetAllTracks();
                var subfolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                string normalizedParent = parentPath.Replace('/', '\\').TrimEnd('\\');
                string searchPrefix = normalizedParent + "\\";

                foreach (var track in allTracks)
                {
                    if (string.IsNullOrEmpty(track.FilePath)) continue;
                    
                    string trackDir = Path.GetDirectoryName(track.FilePath)?.Replace('/', '\\');
                    if (string.IsNullOrEmpty(trackDir)) continue;

                    // 只有当路径完全匹配或者是其子路径（且以分隔符开头）时才处理
                    if (trackDir.StartsWith(searchPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        // 提取 searchPrefix 之后的第一级目录名
                        string relative = trackDir.Substring(searchPrefix.Length);
                        if (!string.IsNullOrEmpty(relative))
                        {
                            string firstLevel = relative.Split('\\')[0];
                            subfolders.Add(Path.Combine(normalizedParent, firstLevel));
                        }
                    }
                }

                return subfolders.Select(f => new SubfolderItem 
                { 
                    Name = Path.GetFileName(f), 
                    Path = f,
                    IsRoot = false 
                })
                .OrderBy(f => f.Name)
                .ToList();
            });
        }

        public IEnumerable<SubfolderItem> GetBreadcrumbs(string currentPath)
        {
            if (string.IsNullOrEmpty(currentPath)) yield break;

            var roots = _dbHelper.GetMusicFolders();
            string root = roots.FirstOrDefault(r => 
            {
                string normRoot = r.Replace('/', '\\').TrimEnd('\\');
                string normPath = currentPath.Replace('/', '\\').TrimEnd('\\');
                return normPath.Equals(normRoot, StringComparison.OrdinalIgnoreCase) || 
                       normPath.StartsWith(normRoot + "\\", StringComparison.OrdinalIgnoreCase);
            });
            
            if (string.IsNullOrEmpty(root))
            {
                yield return new SubfolderItem { Name = Path.GetFileName(currentPath) ?? currentPath, Path = currentPath, IsRoot = true };
                yield break;
            }

            // 从 root 开始构建
            yield return new SubfolderItem { Name = Path.GetFileName(root) ?? root, Path = root, IsRoot = true };

            if (currentPath.Length > root.Length)
            {
                string relative = currentPath.Substring(root.Length).TrimStart('\\', '/');
                string currentBuild = root;
                var parts = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    currentBuild = Path.Combine(currentBuild, part);
                    yield return new SubfolderItem { Name = part, Path = currentBuild, IsRoot = false };
                }
            }
        }
    }
}
