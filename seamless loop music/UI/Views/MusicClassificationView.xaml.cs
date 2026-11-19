using System.Windows.Controls;
using System.Windows.Input;
using seamless_loop_music.Models;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    public partial class MusicClassificationView : UserControl
    {
        public MusicClassificationView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 双击结果行播放该曲目。只响应真实数据行，避免双击表头/空白区域误触发。
        /// </summary>
        private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(ResultList.SelectedItem is TrackClassificationResult result)) return;

            // 双击落在表头或空白处时 SelectedItem 可能仍是旧行，用命中测试确认点在数据行上
            var source = e.OriginalSource as System.Windows.DependencyObject;
            while (source != null && !(source is ListViewItem))
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            if (source == null) return;

            var vm = DataContext as MusicClassificationViewModel;
            if (vm?.PlayCommand.CanExecute(result) == true)
            {
                vm.PlayCommand.Execute(result);
                e.Handled = true;
            }
        }
    }
}
