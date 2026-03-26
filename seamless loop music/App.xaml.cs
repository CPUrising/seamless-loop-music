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
    public partial class App : PrismApplication
    {
        public App()
        {
            try {
                System.IO.File.WriteAllText("start_log.txt", "App constructor start");
            } catch {}
            
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                System.IO.File.WriteAllText("crash_log.txt", e.ExceptionObject.ToString());
            };
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<IDatabaseHelper, DatabaseHelper>();
            containerRegistry.RegisterSingleton<ITrackRepository, TrackRepository>();
            containerRegistry.RegisterSingleton<IPlaylistRepository, PlaylistRepository>();

            containerRegistry.RegisterSingleton<IPlaybackService, PlaybackService>();
            containerRegistry.RegisterSingleton<IPlaylistManager, PlaylistManager>();
            containerRegistry.RegisterSingleton<ILoopAnalysisService, LoopAnalysisService>();

            containerRegistry.RegisterForNavigation<LibraryView, LibraryViewModel>();
            containerRegistry.RegisterForNavigation<DetailView, DetailViewModel>();
            
            containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();
            containerRegistry.RegisterSingleton<IPlaylistManagerService, PlaylistManagerService>();
        }

        protected override void OnInitialized()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                System.IO.File.WriteAllText("crash_log.txt", e.ExceptionObject.ToString());
            };
            
            try 
            {
                // Initialize database before anything else
                var db = Container.Resolve<IDatabaseHelper>();
                db.InitializeDatabase();

                base.OnInitialized();

                var regionManager = Container.Resolve<IRegionManager>();
                regionManager.RequestNavigate("MainContentRegion", "LibraryView");
                regionManager.RequestNavigate("SidebarRegion", "PlaylistSidebar"); 
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("crash_log.txt", ex.ToString());
                MessageBox.Show("Initialization error. Check crash_log.txt for details.");
            }
        }
    }
}
