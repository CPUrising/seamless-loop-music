using System.Windows;
using System.Windows.Controls;

namespace seamless_loop_music.UI.Controls
{
    public partial class TrackInfoControl : UserControl
    {
        public static readonly DependencyProperty ArtSizeProperty = DependencyProperty.Register(
            "ArtSize", typeof(double), typeof(TrackInfoControl), new PropertyMetadata(48.0));

        public static readonly DependencyProperty TitleFontSizeProperty = DependencyProperty.Register(
            "TitleFontSize", typeof(double), typeof(TrackInfoControl), new PropertyMetadata(14.0));

        public static readonly DependencyProperty SubtitleFontSizeProperty = DependencyProperty.Register(
            "SubtitleFontSize", typeof(double), typeof(TrackInfoControl), new PropertyMetadata(12.0));

        public static readonly DependencyProperty IsClickableProperty = DependencyProperty.Register(
            "IsClickable", typeof(bool), typeof(TrackInfoControl), new PropertyMetadata(false));

        public static readonly DependencyProperty ShowAlbumProperty = DependencyProperty.Register(
            "ShowAlbum", typeof(bool), typeof(TrackInfoControl), new PropertyMetadata(true));

        public static readonly DependencyProperty CoverImageProperty = DependencyProperty.Register(
            "CoverImage", typeof(System.Windows.Media.ImageSource), typeof(TrackInfoControl), new PropertyMetadata(null));

        public double ArtSize { get => (double)GetValue(ArtSizeProperty); set => SetValue(ArtSizeProperty, value); }
        public double TitleFontSize { get => (double)GetValue(TitleFontSizeProperty); set => SetValue(TitleFontSizeProperty, value); }
        public double SubtitleFontSize { get => (double)GetValue(SubtitleFontSizeProperty); set => SetValue(SubtitleFontSizeProperty, value); }
        public bool IsClickable { get => (bool)GetValue(IsClickableProperty); set => SetValue(IsClickableProperty, value); }
        public bool ShowAlbum { get => (bool)GetValue(ShowAlbumProperty); set => SetValue(ShowAlbumProperty, value); }
        public System.Windows.Media.ImageSource CoverImage { get => (System.Windows.Media.ImageSource)GetValue(CoverImageProperty); set => SetValue(CoverImageProperty, value); }

        public TrackInfoControl()
        {
            InitializeComponent();
        }
    }
}
