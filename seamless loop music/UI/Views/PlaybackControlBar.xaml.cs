using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    /// <summary>
    /// PlaybackControlBar.xaml 的交互逻辑
    /// </summary>
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
                vm.IsDragging = false;
                // 拖动结束，执行 Seek
                vm.SeekCommand.Execute(ProgressBar.Value);
            }
        }

        private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is PlaybackControlBarViewModel vm && !vm.IsUpdating && !vm.IsDragging)
            {
                // 点击跳转按钮引起的逻辑
                vm.SeekCommand.Execute(e.NewValue);
            }
        }
    }
}
