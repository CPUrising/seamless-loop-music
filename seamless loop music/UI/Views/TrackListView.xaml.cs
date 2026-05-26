using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Prism.Events;
using Prism.Ioc;
using seamless_loop_music.Events;
using seamless_loop_music.Models;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    public partial class TrackListView : UserControl
    {
        public TrackListView()
        {
            InitializeComponent();
            
            // 订阅外部滚动请求事件
            try
            {
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator.GetEvent<ScrollToTrackEvent>().Subscribe(track => 
                {
                    if (track == null) return;
                    
                    // 等待 UI 渲染，然后在后台优先级执行滚动
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        var targetItem = TrackList.Items.Cast<object>()
                            .OfType<MusicTrack>()
                            .FirstOrDefault(t => t.Id == track.Id);
                            
                        if (targetItem != null)
                        {
                            TrackList.ScrollIntoView(targetItem);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }, ThreadOption.UIThread);
            }
            catch { }
        }

        private void TrackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TrackList.SelectedItem != null)
            {
                // 延迟一点执行滚动，确保虚拟化容器已经准备好
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    TrackList.ScrollIntoView(TrackList.SelectedItem);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void TrackList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TrackList.SelectedItem is MusicTrack track)
            {
                // 1. 通过列表找到当前被双击的那个物理行容器（ListBoxItem）
                var listBoxItem = TrackList.ItemContainerGenerator.ContainerFromItem(track) as ListBoxItem;

                if (listBoxItem != null)
                {
                    // 在播放前，强行将它的选中状态抹去
                    listBoxItem.IsSelected = false;
                }
                var vm = DataContext as TrackListViewModel;
                vm?.PlayCommand.Execute(track);
                e.Handled = true;
            }
        }

        private void TrackList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                // 查找内部的 ScrollViewer
                var scrollViewer = GetScrollViewer(listBox);
                if (scrollViewer != null)
                {
                    // 拦截默认滚动，手动控制
                    if (e.Delta > 0)
                        scrollViewer.LineUp();
                    else
                        scrollViewer.LineDown();

                    e.Handled = true;
                }
            }
        }

        private static ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
