using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace seamless_loop_music
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        // 引入 Windows API
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 禁止双开逻辑
            _mutex = new Mutex(true, "SeamlessLoopMusic_SingleInstance_Mutex", out bool isNewInstance);
            if (!isNewInstance)
            {
                // 如果发现已有实例，尝试唤醒它
                BringExistingInstanceToFront();
                Current.Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                MessageBox.Show("Fatal Error: " + ((Exception)args.ExceptionObject).Message, "AppDomain Error");

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show("UI Error: " + args.Exception.Message + "\n" + args.Exception.ToString(), "WPF Error");
                args.Handled = true;
            };

            base.OnStartup(e);
        }

        private void BringExistingInstanceToFront()
        {
            Process currentProcess = Process.GetCurrentProcess();
            foreach (Process process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                // 确保找到的是另一个进程，而不是自己
                if (process.Id != currentProcess.Id)
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        // 1. 如果窗口是最小化的，恢复它
                        ShowWindow(hWnd, SW_RESTORE);
                        // 2. 将窗口置于最前端
                        SetForegroundWindow(hWnd);
                    }
                    break;
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}


