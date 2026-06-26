using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Controls
{
    public partial class ProgressControls : UserControl
    {
        public ProgressControls()
        {
            InitializeComponent();
        }

        private void ProgressBar_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                vm.DragStartedCommand?.Execute();
            }
        }

        private void ProgressBar_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                vm.DragCompletedCommand?.Execute();
            }
        }
    }
}
