using System.Windows;
using System.Windows.Controls;

namespace seamless_loop_music.UI.Controls
{
    public partial class PlaybackControls : UserControl
    {
        public static readonly DependencyProperty ButtonSizeProperty = DependencyProperty.Register(
            "ButtonSize", typeof(double), typeof(PlaybackControls), new PropertyMetadata(24.0));

        public static readonly DependencyProperty PlayButtonWidthProperty = DependencyProperty.Register(
            "PlayButtonWidth", typeof(double), typeof(PlaybackControls), new PropertyMetadata(80.0));

        public static readonly DependencyProperty PlayButtonHeightProperty = DependencyProperty.Register(
            "PlayButtonHeight", typeof(double), typeof(PlaybackControls), new PropertyMetadata(40.0));

        public double ButtonSize { get => (double)GetValue(ButtonSizeProperty); set => SetValue(ButtonSizeProperty, value); }
        public double PlayButtonWidth { get => (double)GetValue(PlayButtonWidthProperty); set => SetValue(PlayButtonWidthProperty, value); }
        public double PlayButtonHeight { get => (double)GetValue(PlayButtonHeightProperty); set => SetValue(PlayButtonHeightProperty, value); }

        public PlaybackControls()
        {
            InitializeComponent();
        }
    }
}
