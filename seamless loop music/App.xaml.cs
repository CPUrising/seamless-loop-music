using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Prism.Unity;
using Prism.Ioc;
using seamless_loop_music.Services;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.UI;
using seamless_loop_music.UI.Views;
using Prism.Regions;

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
            // 数据层注册
            containerRegistry.RegisterSingleton<ITrackRepository, TrackRepository>();
            containerRegistry.RegisterSingleton<IPlaylistRepository, PlaylistRepository>();

            // 服务层注册
            containerRegistry.RegisterSingleton<ILoopAnalysisService, LoopAnalysisService>();
            containerRegistry.RegisterSingleton<IPlaybackService, PlaybackService>();
            containerRegistry.RegisterSingleton<IPlaylistManager, PlaylistManager>();
            
            // 兼容性保留 (逐步废弃)
            containerRegistry.RegisterSingleton<IDatabaseHelper, DatabaseHelper>();
            containerRegistry.RegisterSingleton<IPlaylistManagerService, PlaylistManagerService>();
            containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();

            // 视图注册 (用于导航)
            containerRegistry.RegisterForNavigation<PlaylistSidebar>(); // 暂时继续使用原 Sidebar
            containerRegistry.RegisterForNavigation<LibraryView>();
            containerRegistry.RegisterForNavigation<DetailView>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            // 初始化区域导航
            var regionManager = Container.Resolve<IRegionManager>();
            regionManager.RequestNavigate("SidebarRegion", nameof(PlaylistSidebar));
            regionManager.RequestNavigate("MainContentRegion", nameof(LibraryView));
        }
    }
}
