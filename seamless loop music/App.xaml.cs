using System;
using System.Windows;
using Prism.Unity;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Regions;
using seamless_loop_music.Services;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.UI;
using seamless_loop_music.UI.Views;
using seamless_loop_music.UI.ViewModels;

namespace seamless_loop_music
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 基础数据访问
            containerRegistry.RegisterSingleton<IDatabaseHelper, DatabaseHelper>();
            containerRegistry.RegisterSingleton<ITrackRepository, TrackRepository>();
            containerRegistry.RegisterSingleton<IPlaylistRepository, PlaylistRepository>();

            // 核心播放与逻辑服务 (New Architecture)
            containerRegistry.RegisterSingleton<IPlaybackService, PlaybackService>();
            containerRegistry.RegisterSingleton<IPlaylistManager, PlaylistManager>();
            containerRegistry.RegisterSingleton<ILoopAnalysisService, LoopAnalysisService>();

            // 路由导航支持
            containerRegistry.RegisterForNavigation<LibraryView, LibraryViewModel>();
            containerRegistry.RegisterForNavigation<DetailView, DetailViewModel>();
            
            // 兼容性注册 (Legacy support for un-refactored windows)
            containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();
            containerRegistry.RegisterSingleton<IPlaylistManagerService, PlaylistManagerService>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            // 初始导航到库视图
            var regionManager = Container.Resolve<IRegionManager>();
            regionManager.RequestNavigate("MainContentRegion", "LibraryView");
            
            // 初始导航到侧边栏（假设 Sidebar 是一个 View）
            // regionManager.RequestNavigate("SidebarRegion", "PlaylistSidebar"); 
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            // 如果有外部模块再配置
        }
    }
}
