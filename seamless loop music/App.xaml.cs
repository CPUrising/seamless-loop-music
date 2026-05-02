using System;
using System.Windows;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Prism.Unity;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Regions;
using Prism.Events;
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
        private static Mutex _mutex = null;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "SeamlessLoopMusicAppMutex_SingleInstance";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running. Bring existing instance to front.
                Process currentProcess = Process.GetCurrentProcess();
                foreach (Process process in Process.GetProcessesByName(currentProcess.ProcessName))
                {
                    if (process.Id != currentProcess.Id)
                    {
                        IntPtr hWnd = process.MainWindowHandle;
                        if (hWnd != IntPtr.Zero)
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                            SetForegroundWindow(hWnd);
                        }
                        break;
                    }
                }

                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

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
            containerRegistry.RegisterSingleton<IQueueManager, QueueManager>();
            containerRegistry.RegisterSingleton<ILoopAnalysisService, LoopAnalysisService>();
            containerRegistry.RegisterSingleton<ISearchService, SearchService>();
            containerRegistry.RegisterSingleton<TrackMetadataService>();
            containerRegistry.RegisterSingleton<ITaskbarService, TaskbarService>();
            containerRegistry.RegisterSingleton<INotifyIconService, NotifyIconService>();

            containerRegistry.RegisterForNavigation<LibraryView, LibraryViewModel>();
            containerRegistry.RegisterForNavigation<DetailView, DetailViewModel>();
            containerRegistry.RegisterForNavigation<TrackListView, TrackListViewModel>();
            containerRegistry.RegisterForNavigation<NowPlayingView, NowPlayingViewModel>();
            
            containerRegistry.RegisterSingleton<LoopWorkspaceViewModel>();
            containerRegistry.RegisterSingleton<TrayControlsViewModel>();
            containerRegistry.RegisterSingleton<TrayControlsWindow>();
            
            containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();
            containerRegistry.RegisterSingleton<IPlaylistManagerService, PlaylistManagerService>();
            containerRegistry.RegisterSingleton<IAppStateService, AppStateService>();
        }

        protected override void OnInitialized()
        {
            LocalizationService.EventAggregator = Container.Resolve<IEventAggregator>();

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

                var notifyIconService = Container.Resolve<INotifyIconService>();
                notifyIconService.Initialize();

                // Perform startup cleanup and restore last app state
                Task.Run(async () => 
                {
                    // Delay slightly to ensure UI regions are ready
                    await Task.Delay(200);
                    
                    try 
                    {
                        // 1. Cleanup missing files (optional but good for health)
                        var playlistManager = Container.Resolve<IPlaylistManagerService>();
                        await playlistManager.CleanupMissingTracksAsync();
                    }
                    catch (Exception ex) { Debug.WriteLine($"Cleanup error: {ex.Message}"); }

                    // 2. Restore state
                    var appState = Container.Resolve<IAppStateService>();
                    await appState.RestoreStateAsync();
                });
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("crash_log.txt", ex.ToString());
                MessageBox.Show("Initialization error. Check crash_log.txt for details.");
            }
        }
    }
}
