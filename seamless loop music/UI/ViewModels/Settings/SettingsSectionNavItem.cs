namespace seamless_loop_music.UI.ViewModels.Settings
{
    /// <summary>
    /// 设置页左侧一级菜单项。只负责描述导航目标，不承载具体设置值。
    /// </summary>
    public class SettingsSectionNavItem
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string ViewName { get; set; }
    }
}
