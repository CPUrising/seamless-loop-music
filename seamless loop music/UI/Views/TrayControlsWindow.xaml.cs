using System;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music.UI.Views
{
    public partial class TrayControlsWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Counter-based deactivation suppression: each Show absorbs one phantom Deactivated event
        /// caused by the system tray's focus management.
        /// </summary>
        private int _suppressDeactivationCount;

        public TrayControlsWindow(TrayControlsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        public void ShowAtTaskbar()
        {
            // Absorb the next phantom Deactivated event (caused by tray icon focus reset)
            _suppressDeactivationCount = 1;

            SetGeometry();
            this.Show();

            try
            {
                var helper = new WindowInteropHelper(this);
                SetForegroundWindow(helper.Handle);
            }
            catch { }

            this.Activate();
            this.Focus();
            this.Topmost = true;
        }

        private void SetGeometry()
        {
            // Use WPF's SystemParameters.WorkArea (in device-independent units)
            // instead of WinForms Screen.WorkingArea (physical pixels) to handle DPI scaling correctly
            var workingArea = SystemParameters.WorkArea;

            const double windowWidth = 220;  // 200 Width + 2 × 10 Margin
            double windowHeight = this.Height;
            if (double.IsNaN(windowHeight)) windowHeight = 270;

            // Default: position at bottom-right corner of the working area
            double left = workingArea.Right - windowWidth - 10;
            double top = workingArea.Bottom - windowHeight - 5;

            // If taskbar is at top (workArea.Top > 0), position at top-right
            if (workingArea.Top > 5)
            {
                top = workingArea.Top + 5;
            }
            // If taskbar is at left (workArea.Left > 0), position at bottom-left
            else if (workingArea.Left > 5)
            {
                left = workingArea.Left + 5;
                top = workingArea.Bottom - windowHeight - 10;
            }

            this.Left = left;
            this.Top = top;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // If we have suppression credits remaining, consume one and ignore this Deactivated.
            // This absorbs the phantom deactivation caused by the system tray's focus reset.
            if (_suppressDeactivationCount > 0)
            {
                _suppressDeactivationCount--;
                return;
            }

            this.Hide();
        }
    }
}
