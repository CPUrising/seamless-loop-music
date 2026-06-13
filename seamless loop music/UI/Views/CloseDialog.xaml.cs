using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace seamless_loop_music.UI.Views
{
    public enum CloseAction
    {
        None,
        MinimizeToTray,
        Exit
    }

    public partial class CloseDialog : Window
    {
        public CloseAction Result { get; private set; } = CloseAction.None;
        public bool RememberChoice => RememberCheck.IsChecked == true;

        public CloseDialog()
        {
            InitializeComponent();
        }

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.MinimizeToTray;
            Close();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.Exit;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.None;
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
