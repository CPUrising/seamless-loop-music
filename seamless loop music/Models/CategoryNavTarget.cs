using System;

namespace seamless_loop_music.Models
{
    public class CategoryNavTarget
    {
        public string Name { get; set; }
        public string Icon { get; set; } // 存储 Emoji、Icon 文字或者是 IconGeometry
        public CategoryType Type { get; set; }
    }
}
