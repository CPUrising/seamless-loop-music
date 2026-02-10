using System;
using System.Windows;

namespace seamless_loop_music
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                MessageBox.Show("Fatal Error: " + ((Exception)args.ExceptionObject).Message, "AppDomain Error");

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show("UI Error: " + args.Exception.Message + "\n" + args.Exception.ToString(), "WPF Error");
                args.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}
