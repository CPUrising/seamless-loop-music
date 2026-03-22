using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Prism.Unity;
using Prism.Ioc;
using seamless_loop_music.Services;
using seamless_loop_music.Data;
using seamless_loop_music.UI;

namespace seamless_loop_music
{
    public partial class App : PrismApplication
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 全局异常处理
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                LogException((Exception)args.ExceptionObject, "Fatal Domain Error");

            DispatcherUnhandledException += (s, args) =>
            {
                LogException(args.Exception, "UI Dispatcher Error");
                args.Handled = true;
            };

            base.OnStartup(e);
        }

        private void LogException(Exception ex, string title)
        {
            MessageBox.Show($"{title}:\n{ex.Message}\n\n{ex.StackTrace}", "Seamless Loop Music Error");
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 核心服务注册 (单例)
            containerRegistry.RegisterSingleton<IDatabaseHelper, DatabaseHelper>();
            containerRegistry.RegisterSingleton<IPlaylistManagerService, PlaylistManagerService>();
            containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();
        }
    }
}
