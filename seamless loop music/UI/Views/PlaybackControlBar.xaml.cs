using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    public partial class PlaybackControlBar : UserControl
    {
        public PlaybackControlBar()
        {
            InitializeComponent();
        }

        private void ProgressBar_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                vm.IsDragging = true;
            }
        }

        private void ProgressBar_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is PlaybackControlBarViewModel vm)
            {
                vm.SeekCommand.Execute(ProgressBar.Value);
                vm.IsDragging = false;
            }
        }
    }
}
