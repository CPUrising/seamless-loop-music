using System;

namespace seamless_loop_music.Models
{
    /// <summary>
    /// 表示一个歌单（物理文件夹关联或纯虚拟逻辑歌单）
    /// </summary>
    public class PlaylistFolder
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsFolderLinked { get; set; }
        public int SongCount { get; set; }
        public DateTime CreatedAt { get; set; }

        public override string ToString() => Name;
    }
}
