using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace seamless_loop_music.UI.Views.Settings
{
    public partial class ClearLocalSyncDataDialog : Window
    {
        public bool ClearPlaylists { get; private set; }
        public bool ClearLoopPoints { get; private set; }
        public bool ClearRatings { get; private set; }

        public ClearLocalSyncDataDialog(bool clearPlaylists, bool clearLoopPoints, bool clearRatings)
        {
            InitializeComponent();

            PlaylistsCheckBox.IsChecked = clearPlaylists;
            LoopPointsCheckBox.IsChecked = clearLoopPoints;
            RatingsCheckBox.IsChecked = clearRatings;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            ClearPlaylists = PlaylistsCheckBox.IsChecked == true;
            ClearLoopPoints = LoopPointsCheckBox.IsChecked == true;
            ClearRatings = RatingsCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Dialog_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private static bool IsInteractiveElement(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }
    }
}
