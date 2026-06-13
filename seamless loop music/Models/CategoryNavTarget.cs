using System;

namespace seamless_loop_music.Models
{
    public class CategoryNavTarget
    {
        public string Name { get; set; }
        public string Icon { get; set; } // 存储 Emoji、Icon 文字或者是 IconGeometry
        public CategoryType Type { get; set; }
        public bool IsSettings { get; set; } // 侧边栏里的设置入口，不参与分类过滤
    }
}
