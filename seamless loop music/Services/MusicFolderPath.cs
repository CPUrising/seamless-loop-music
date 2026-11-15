using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace seamless_loop_music.Services
{
    /// <summary>
    /// 音乐文件夹添加计划：由纯函数算出，调用方负责落库。
    /// </summary>
    public sealed class MusicFolderAddPlan
    {
        public List<string> ToAdd { get; } = new List<string>();
        public List<string> ToRemove { get; } = new List<string>();
        public List<string> SkippedCovered { get; } = new List<string>();
        public List<string> Rejected { get; } = new List<string>();
    }

    /// <summary>
    /// 音乐文件夹路径规则：归一化、父子判定与添加计划。不访问数据库，便于单测。
    /// </summary>
    public static class MusicFolderPath
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(path.Trim())
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                string root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) && full.Length > root.Length)
                {
                    full = full.TrimEnd(Path.DirectorySeparatorChar);
                }

                return full;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsSameOrAncestor(string ancestor, string child)
        {
            string parent = Normalize(ancestor);
            string target = Normalize(child);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(target))
            {
                return false;
            }

            if (string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string prefix = parent.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? parent
                : parent + Path.DirectorySeparatorChar;

            return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static MusicFolderAddPlan BuildPlan(IEnumerable<string> existingFolders, IEnumerable<string> candidateFolders)
        {
            var plan = new MusicFolderAddPlan();

            var existing = (existingFolders ?? Enumerable.Empty<string>())
                .Select(Normalize)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidates = (candidateFolders ?? Enumerable.Empty<string>())
                .Select(Normalize)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                // 上层目录更短，先处理以便用父目录吞并子目录
                .OrderBy(p => p.Length)
                .ToList();

            var selected = new List<string>();
            foreach (var candidate in candidates)
            {
                if (!Directory.Exists(candidate))
                {
                    plan.Rejected.Add(candidate);
                    continue;
                }

                if (existing.Any(folder => IsSameOrAncestor(folder, candidate)))
                {
                    plan.SkippedCovered.Add(candidate);
                    continue;
                }

                if (selected.Any(folder => IsSameOrAncestor(folder, candidate)))
                {
                    plan.SkippedCovered.Add(candidate);
                    continue;
                }

                selected.Add(candidate);
            }

            foreach (var candidate in selected)
            {
                plan.ToRemove.AddRange(existing.Where(folder => IsSameOrAncestor(candidate, folder)));
            }

            plan.ToAdd.AddRange(selected);
            return plan;
        }
    }
}
