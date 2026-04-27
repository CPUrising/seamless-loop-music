using System;
using System.Windows;
using System.Windows.Forms;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    public partial class TrayControlsWindow : Window
    {
        public TrayControlsWindow(TrayControlsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        public void ShowAtTaskbar()
        {
            SetGeometry();
            this.Show();
            this.Activate();
            this.Topmost = true;
        }

        private void SetGeometry()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            var workingArea = screen.WorkingArea;
            var bounds = screen.Bounds;

            double left = 0;
            double top = 0;

            // Detect taskbar position based on working area vs screen bounds
            if (workingArea.Bottom < bounds.Bottom) // Bottom
            {
                left = workingArea.Right - this.Width - 10;
                top = workingArea.Bottom - this.Height - 5;
            }
            else if (workingArea.Top > bounds.Top) // Top
            {
                left = workingArea.Right - this.Width - 10;
                top = workingArea.Top + 5;
            }
            else if (workingArea.Left > bounds.Left) // Left
            {
                left = workingArea.Left + 5;
                top = workingArea.Bottom - this.Height - 10;
            }
            else if (workingArea.Right < bounds.Right) // Right
            {
                left = workingArea.Right - this.Width - 5;
                top = workingArea.Bottom - this.Height - 10;
            }
            else // Default to Bottom
            {
                left = workingArea.Right - this.Width - 10;
                top = workingArea.Bottom - this.Height - 5;
            }

            this.Left = left;
            this.Top = top;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }
    }
}
