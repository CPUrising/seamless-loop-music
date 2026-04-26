using System.Windows.Controls;

namespace seamless_loop_music.UI.Views
{
    public partial class TrackListView : UserControl
    {
        public TrackListView()
        {
            InitializeComponent();
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
    }
}
