using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace seamless_loop_music.UI.Views.Settings
{
    public partial class SettingsMusicView : UserControl
    {
        public SettingsMusicView()
        {
            InitializeComponent();
        }

        private void FolderListBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var border = (Border)sender;
            border.Clip = new RectangleGeometry(
                new Rect(0, 0, border.ActualWidth, border.ActualHeight),
                radiusX: 11,
                radiusY: 11
            );
        }
    }
}