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

            const double windowWidth = 220;  // 200 Width + 2 × 10 Margin
            double windowHeight = this.Height;
            if (double.IsNaN(windowHeight)) windowHeight = 270;

            double left;
            double top;

            if (workingArea.Bottom < bounds.Bottom) // Bottom
            {
                left = workingArea.Right - windowWidth - 10;
                top = workingArea.Bottom - windowHeight - 5;
            }
            else if (workingArea.Top > bounds.Top) // Top
            {
                left = workingArea.Right - windowWidth - 10;
                top = workingArea.Top + 5;
            }
            else if (workingArea.Left > bounds.Left) // Left
            {
                left = workingArea.Left + 5;
                top = workingArea.Bottom - windowHeight - 10;
            }
            else if (workingArea.Right < bounds.Right) // Right
            {
                left = workingArea.Right - windowWidth - 5;
                top = workingArea.Bottom - windowHeight - 10;
            }
            else // Default to Bottom
            {
                left = workingArea.Right - windowWidth - 10;
                top = workingArea.Bottom - windowHeight - 5;
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
