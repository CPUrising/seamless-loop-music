using System;

namespace seamless_loop_music.Models
{
    public enum CategoryType
    {
        Album,
        Artist,
        Playlist,
        Folder
    }

    public class CategoryItem
    {
        public int Id { get; set; } // 存储关联项的 ID (如 PlaylistId)
        public string Name { get; set; }
        public string Icon { get; set; } // 图标字符串 (如 🎶, ❤️)
        public string ImagePath { get; set; } // 专辑封面或艺术家头像
        public string FolderPath { get; set; } // 文件夹路径 (仅在 Type 为 Folder 时使用)
        public string Description { get; set; } // 辅助描述 (如专辑的艺术家名)
        public CategoryType Type { get; set; }
    }
}
