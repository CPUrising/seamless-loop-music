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
            // Get working area (desktop area minus taskbar)
            var workingArea = SystemParameters.WorkArea;
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            double left = workingArea.Right - this.Width;
            double top = workingArea.Bottom - this.Height;

            // Detect taskbar position
            if (workingArea.Top > 0) // Taskbar at top
            {
                left = workingArea.Right - this.Width;
                top = workingArea.Top;
            }
            else if (workingArea.Left > 0) // Taskbar at left
            {
                left = workingArea.Left;
                top = workingArea.Bottom - this.Height;
            }
            else if (workingArea.Right < screenWidth) // Taskbar at right
            {
                left = workingArea.Right - this.Width;
                top = workingArea.Bottom - this.Height;
            }
            else // Taskbar at bottom (default)
            {
                left = workingArea.Right - this.Width;
                top = workingArea.Bottom - this.Height;
            }

            // Add small offset from edges
            this.Left = left - 5;
            this.Top = top - 5;

            // Adjust for taskbar at top
            if (workingArea.Top > 0) this.Top += 10;
            // Adjust for taskbar at left
            if (workingArea.Left > 0) this.Left += 10;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }
    }
}
