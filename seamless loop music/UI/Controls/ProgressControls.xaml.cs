using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace seamless_loop_music.UI.Controls
{
    public partial class ProgressControls : UserControl
    {
        private bool _isDragging = false;

        public ProgressControls()
        {
            InitializeComponent();
            // Use AddHandler to ensure we catch bubbling events from the Thumb
            ProgressBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(ProgressBar_DragStarted), true);
            ProgressBar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(ProgressBar_DragCompleted), true);
        }

        private void ProgressBar_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isDragging = true;
            if (DataContext is ViewModels.PlaybackControlBarViewModel vm)
            {
                vm.IsDragging = true;
            }
        }

        private void ProgressBar_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDragging = false;
            // Notify ViewModel or perform seek
            if (DataContext is ViewModels.PlaybackControlBarViewModel vm)
            {
                vm.IsDragging = false;
                vm.SeekToProgress(ProgressBar.Value);
            }
            else if (DataContext is ViewModels.TrayControlsViewModel tvm)
            {
                tvm.SeekToProgress(ProgressBar.Value);
            }
        }

        private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDragging) return;

            if (DataContext is ViewModels.PlaybackControlBarViewModel vm)
            {
                if (!vm.IsUpdating)
                {
                    vm.SeekToProgress(e.NewValue);
                }
            }
            else if (DataContext is ViewModels.TrayControlsViewModel tvm)
            {
                // TrayControlsViewModel doesn't have IsUpdating yet, 
                // but we can add it or just handle DragCompleted
            }
        }
    }
}
