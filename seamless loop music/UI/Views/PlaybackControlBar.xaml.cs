using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    public partial class PlaybackControlBar : UserControl
    {
        public PlaybackControlBar()
        {
            InitializeComponent();
        }

        private void ProgressBar_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                vm.IsDragging = true;
            }
        }

        private void ProgressBar_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                vm.SeekCommand.Execute(ProgressBar.Value);
                vm.IsDragging = false;
            }
        }

        private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                // 如果不是由引擎同步引起的更新 (IsUpdating == false)
                // 且不是用户正在拖拽 (IsDragging == false，拖拽由 DragCompleted 结算)
                // 那么这一定是一次点击音轨引发的跳转
                if (!vm.IsUpdating && !vm.IsDragging)
                {
                    vm.SeekCommand.Execute(e.NewValue);
                }
            }
        }
    }
}
