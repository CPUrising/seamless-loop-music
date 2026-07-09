using System.Windows;
using seamless_loop_music.UI.Views;

namespace seamless_loop_music
{
    public static class AppDialogService
    {
        public static MessageBoxResult Show(string message)
        {
            return Show(message, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title)
        {
            return Show(message, title, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons)
        {
            return Show(message, title, buttons, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                return app.Dispatcher.Invoke(() => Show(message, title, buttons, image));
            }

            var dialog = new AppMessageDialog(message, title, buttons, image);
            var owner = ResolveOwner();

            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        private static Window ResolveOwner()
        {
            var current = Application.Current;
            if (current == null)
            {
                return null;
            }

            foreach (Window window in current.Windows)
            {
                if (window.IsActive)
                {
                    return window;
                }
            }

            if (current.MainWindow != null)
            {
                return current.MainWindow;
            }

            foreach (Window window in current.Windows)
            {
                return window;
            }

            return null;
        }
    }
}
