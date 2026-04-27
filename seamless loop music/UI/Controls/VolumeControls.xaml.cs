using System.Windows;
using System.Windows.Controls;

namespace seamless_loop_music.UI.Controls
{
    public partial class VolumeControls : UserControl
    {
        public static readonly DependencyProperty SliderWidthProperty = DependencyProperty.Register(
            "SliderWidth", typeof(double), typeof(VolumeControls), new PropertyMetadata(100.0));

        public double SliderWidth { get => (double)GetValue(SliderWidthProperty); set => SetValue(SliderWidthProperty, value); }

        public VolumeControls()
        {
            InitializeComponent();
        }
    }
}
