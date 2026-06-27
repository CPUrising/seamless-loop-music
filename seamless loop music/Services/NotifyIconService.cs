using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Prism.Events;
using Prism.Ioc;
using seamless_loop_music.Events;
using seamless_loop_music.Models;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace seamless_loop_music.Services
{
    public class NotifyIconService : INotifyIconService
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaybackService _playbackService;
        private readonly IAppStateService _appStateService;
        private readonly Prism.Ioc.IContainerProvider _container;
        private UI.Views.TrayControlsWindow _trayWindow;

        // Native WPF ContextMenu for right-click
        private ContextMenu _trayContextMenu;
        private MenuItem _playPauseItem;
        private MenuItem _prevItem;
        private MenuItem _nextItem;
        private MenuItem _singleLoopItem;
        private MenuItem _listLoopItem;
        private MenuItem _shuffleItem;
        private MenuItem _seamlessLoopItem;
        private MenuItem _exitItem;
        private MenuItem _openMainItem;
        private MenuItem _playModeMenu;

        // Hidden helper window to anchor ContextMenu and handle its Deactivated close
        private Window _contextMenuHost;

        public NotifyIconService(IEventAggregator eventAggregator, IPlaybackService playbackService, IAppStateService appStateService, Prism.Ioc.IContainerProvider container)
        {
            _eventAggregator = eventAggregator;
            _playbackService = playbackService;
            _appStateService = appStateService;
            _container = container;

            _notifyIcon = new NotifyIcon();
        }

        public void Initialize()
        {
            try
            {
                // Load icon from resources
                var iconUri = new Uri("pack://application:,,,/Resources/app_icon.ico");
                var iconStream = Application.GetResourceStream(iconUri)?.Stream;
                if (iconStream != null)
                {
                    _notifyIcon.Icon = new Icon(iconStream);
                }
            }
            catch
            {
                // Fallback or ignore if icon not found
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.Text = "Seamless Loop Music";
            _notifyIcon.Visible = true;

            _notifyIcon.MouseClick += NotifyIcon_MouseClick;
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            BuildContextMenu();
            CreateContextMenuHost();

            // Subscribe to playback state changes to refresh tray context menu in real-time
            _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Subscribe(OnPlaybackStateChanged);
        }

        private void OnPlaybackStateChanged(NAudio.Wave.PlaybackState state)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshContextMenuState();
            });
        }

        /// <summary>
        /// Build a native WPF ContextMenu for right-click on the tray icon.
        /// Using a native ContextMenu avoids the focus-reset issue that plagues custom windows on right-click.
        /// </summary>
        private void BuildContextMenu()
        {
            var loc = LocalizationService.Instance;

            var trayStyle = Application.Current.TryFindResource("TrayMenuItemStyle") as Style;

            _playPauseItem = new MenuItem { Header = loc["MenuPlay"], StaysOpenOnClick = true };
            if (trayStyle != null) _playPauseItem.Style = trayStyle;
            _playPauseItem.Click += (s, e) =>
            {
                if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                    _playbackService.Pause();
                else
                    _playbackService.Play();
                
                RefreshContextMenuState();
            };

            _prevItem = new MenuItem { Header = loc["MenuPrevious"], StaysOpenOnClick = true };
            if (trayStyle != null) _prevItem.Style = trayStyle;
            _prevItem.Click += (s, e) => 
            {
                _playbackService.Previous();
                RefreshContextMenuState();
            };

            _nextItem = new MenuItem { Header = loc["MenuNext"], StaysOpenOnClick = true };
            if (trayStyle != null) _nextItem.Style = trayStyle;
            _nextItem.Click += (s, e) => 
            {
                _playbackService.Next();
                RefreshContextMenuState();
            };

            // Play mode sub-items
            _singleLoopItem = new MenuItem { Header = loc["TipPlayModeSingle"], IsCheckable = true, StaysOpenOnClick = true };
            if (trayStyle != null) _singleLoopItem.Style = trayStyle;
            _singleLoopItem.Click += (s, e) => 
            { 
                _playbackService.PlayMode = PlayMode.SingleLoop; 
                RefreshContextMenuState();
            };

            _listLoopItem = new MenuItem { Header = loc["TipPlayModeList"], IsCheckable = true, StaysOpenOnClick = true };
            if (trayStyle != null) _listLoopItem.Style = trayStyle;
            _listLoopItem.Click += (s, e) => 
            { 
                _playbackService.PlayMode = PlayMode.ListLoop; 
                RefreshContextMenuState();
            };

            _shuffleItem = new MenuItem { Header = loc["TipPlayModeShuffle"], IsCheckable = true, StaysOpenOnClick = true };
            if (trayStyle != null) _shuffleItem.Style = trayStyle;
            _shuffleItem.Click += (s, e) => 
            { 
                _playbackService.PlayMode = PlayMode.Shuffle; 
                RefreshContextMenuState();
            };

            _playModeMenu = new MenuItem { Header = loc["TrayPlayMode"] };
            if (trayStyle != null) _playModeMenu.Style = trayStyle;
            _playModeMenu.Items.Add(_singleLoopItem);
            _playModeMenu.Items.Add(_listLoopItem);
            _playModeMenu.Items.Add(_shuffleItem);

            _seamlessLoopItem = new MenuItem { Header = loc["FeatureLoop"], IsCheckable = true, StaysOpenOnClick = true };
            if (trayStyle != null) _seamlessLoopItem.Style = trayStyle;
            _seamlessLoopItem.Click += (s, e) =>
            {
                _playbackService.IsSeamlessLoopEnabled = _seamlessLoopItem.IsChecked;
                RefreshContextMenuState();
            };

            _openMainItem = new MenuItem { Header = loc["MenuShowMainWindow"] ?? "显示主窗口" };
            if (trayStyle != null) _openMainItem.Style = trayStyle;
            _openMainItem.Click += (s, e) => ShowMainWindow();

            _exitItem = new MenuItem { Header = loc["MenuExit"] };
            if (trayStyle != null) _exitItem.Style = trayStyle;
            _exitItem.Click += (s, e) =>
            {
                _appStateService.IsExiting = true;
                Application.Current.Shutdown();
            };

            _trayContextMenu = new ContextMenu();
            var contextMenuStyle = Application.Current.TryFindResource("TrayContextMenuStyle") as Style;
            if (contextMenuStyle != null)
            {
                _trayContextMenu.Style = contextMenuStyle;
            }
            _trayContextMenu.Items.Add(_playPauseItem);
            _trayContextMenu.Items.Add(_prevItem);
            _trayContextMenu.Items.Add(_nextItem);
            _trayContextMenu.Items.Add(new Separator());
            _trayContextMenu.Items.Add(_playModeMenu);
            _trayContextMenu.Items.Add(_seamlessLoopItem);
            _trayContextMenu.Items.Add(new Separator());
            _trayContextMenu.Items.Add(_openMainItem);
            _trayContextMenu.Items.Add(_exitItem);
        }

        /// <summary>
        /// Create a tiny invisible helper window that serves as the owner/anchor for the ContextMenu.
        /// When this window is Deactivated (user clicks elsewhere), the ContextMenu auto-closes.
        /// This is the same pattern Dopamine uses (Shell.Activate() after opening ContextMenu).
        /// </summary>
        private void CreateContextMenuHost()
        {
            _contextMenuHost = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                Left = -9999,
                Top = -9999
            };

            _contextMenuHost.Deactivated += (s, e) =>
            {
                _trayContextMenu.IsOpen = false;
            };
        }

        /// <summary>
        /// Refresh all ContextMenu item states (labels, checked states) before showing.
        /// </summary>
        private void RefreshContextMenuState()
        {
            var loc = LocalizationService.Instance;

            bool isPlaying = _playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing;
            _playPauseItem.Header = isPlaying ? loc["MenuPause"] : loc["MenuPlay"];
            _prevItem.Header = loc["MenuPrevious"];
            _nextItem.Header = loc["MenuNext"];
            _exitItem.Header = loc["MenuExit"];
            _openMainItem.Header = loc["MenuShowMainWindow"] ?? "显示主窗口";

            _singleLoopItem.Header = loc["TipPlayModeSingle"];
            _singleLoopItem.IsChecked = _playbackService.PlayMode == PlayMode.SingleLoop;
            _listLoopItem.Header = loc["TipPlayModeList"];
            _listLoopItem.IsChecked = _playbackService.PlayMode == PlayMode.ListLoop;
            _shuffleItem.Header = loc["TipPlayModeShuffle"];
            _shuffleItem.IsChecked = _playbackService.PlayMode == PlayMode.Shuffle;

            _playModeMenu.Header = loc["TrayPlayMode"];

            _seamlessLoopItem.Header = loc["FeatureLoop"];
            _seamlessLoopItem.IsChecked = _playbackService.IsSeamlessLoopEnabled;
        }

        private void ToggleMainWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null) return;

                if (mainWindow.IsVisible)
                    HideMainWindow();
                else
                    ShowMainWindow();
            });
        }

        public void ShowMainWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;

                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false;
                }
            });
        }

        public void HideMainWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.Hide();
                }
            });
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Left-click: show the main window (temporarily changed from ShowTrayControls per CPU's request)
                ShowMainWindow();
            }
            else if (e.Button == MouseButtons.Right)
            {
                // Right-click: show native WPF ContextMenu (avoids focus-reset issue)
                ShowContextMenu();
            }
        }

        private void ShowTrayControls()
        {
            // Use BeginInvoke to defer the show to a later dispatcher frame.
            // This ensures the NotifyIcon's click event has fully unwound from
            // the Windows message queue before we show the window, preventing
            // the system tray's focus management from stealing our window's focus.
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_trayWindow == null)
                    {
                        _trayWindow = _container.Resolve<UI.Views.TrayControlsWindow>();
                    }

                    if (_trayWindow.DataContext is UI.ViewModels.TrayControlsViewModel viewModel)
                    {
                        viewModel.RefreshMenuState();
                    }

                    _trayWindow.ShowAtTaskbar();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TrayIcon] ShowTrayControls ERROR: {ex}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ShowContextMenu()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshContextMenuState();

                // Show and activate the hidden host window so that:
                // 1. ContextMenu has a valid WPF owner
                // 2. Clicking outside triggers Deactivated → closes the menu
                _contextMenuHost.Show();
                _contextMenuHost.Activate();

                _trayContextMenu.IsOpen = true;
            });
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        public void Dispose()
        {
            _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Unsubscribe(OnPlaybackStateChanged);

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            if (_contextMenuHost != null)
            {
                _contextMenuHost.Close();
                _contextMenuHost = null;
            }
        }
    }
}
