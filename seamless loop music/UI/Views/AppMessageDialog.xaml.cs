using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace seamless_loop_music.UI.Views
{
    public partial class AppMessageDialog : Window
    {
        private readonly MessageBoxButton _buttons;

        public MessageBoxResult Result { get; private set; }

        public AppMessageDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            InitializeComponent();

            _buttons = buttons;
            Result = GetDefaultResult(buttons);

            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "提示" : title;
            SubtitleText.Text = string.Empty;
            MessageText.Text = message ?? string.Empty;

            ConfigureVisuals(image);
            ConfigureButtons(buttons);
        }

        private void ConfigureVisuals(MessageBoxImage image)
        {
            switch (image)
            {
                case MessageBoxImage.Warning:
                    IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E5"));
                    IconGlyph.Text = "!";
                    IconGlyph.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B85C00"));
                    MessagePanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF8EF"));
                    MessagePanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0D7B8"));
                    PrimaryButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D13438"));
                    PrimaryButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B12A2D"));
                    break;
                case MessageBoxImage.Error:
                    IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCE8E8"));
                    IconGlyph.Text = "!";
                    IconGlyph.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D13438"));
                    MessagePanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7F7"));
                    MessagePanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2C8C8"));
                    PrimaryButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D13438"));
                    PrimaryButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B12A2D"));
                    break;
                case MessageBoxImage.Question:
                    IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F2F8"));
                    IconGlyph.Text = "?";
                    IconGlyph.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
                    MessagePanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4FAFD"));
                    MessagePanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D6E7F3"));
                    break;
                default:
                    IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F2F8"));
                    IconGlyph.Text = "i";
                    IconGlyph.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
                    break;
            }
        }

        private void ConfigureButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    SecondaryButton.Visibility = Visibility.Collapsed;
                    PrimaryButton.Content = "确定";
                    PrimaryButton.IsDefault = true;
                    break;
                case MessageBoxButton.OKCancel:
                    SecondaryButton.Visibility = Visibility.Visible;
                    SecondaryButton.Content = "取消";
                    PrimaryButton.Content = "确定";
                    PrimaryButton.IsDefault = true;
                    break;
                case MessageBoxButton.YesNo:
                    SecondaryButton.Visibility = Visibility.Visible;
                    SecondaryButton.Content = "否";
                    PrimaryButton.Content = "是";
                    PrimaryButton.IsDefault = true;
                    break;
                default:
                    SecondaryButton.Visibility = Visibility.Collapsed;
                    PrimaryButton.Content = "确定";
                    PrimaryButton.IsDefault = true;
                    break;
            }
        }

        private static MessageBoxResult GetDefaultResult(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    return MessageBoxResult.OK;
                case MessageBoxButton.OKCancel:
                    return MessageBoxResult.Cancel;
                case MessageBoxButton.YesNo:
                    return MessageBoxResult.No;
                default:
                    return MessageBoxResult.OK;
            }
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = _buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK;
            DialogResult = true;
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = _buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
            Close();
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
