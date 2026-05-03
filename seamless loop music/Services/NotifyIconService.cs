using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Prism.Events;
using Prism.Ioc;
using seamless_loop_music.Events;
using Application = System.Windows.Application;

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
        private ToolStripMenuItem _showHideItem;

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

            InitializeContextMenu();
        }

        private void InitializeContextMenu()
        {
            var contextMenu = new ContextMenuStrip();
            contextMenu.Opening += (s, e) => UpdateContextMenuItems();

            _showHideItem = new ToolStripMenuItem(string.Empty, null, (s, e) => ToggleMainWindow());
            
            var playPauseItem = new ToolStripMenuItem(string.Empty, null, (s, e) => {
                if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                    _playbackService.Pause();
                else
                    _playbackService.Play();
            });

            var nextItem = new ToolStripMenuItem(string.Empty, null, (s, e) => _playbackService.Next());
            var prevItem = new ToolStripMenuItem(string.Empty, null, (s, e) => _playbackService.Previous());
            
            var exitItem = new ToolStripMenuItem(string.Empty, null, (s, e) => 
            {
                _appStateService.IsExiting = true;
                Application.Current.Shutdown();
            });

            contextMenu.Items.Add(_showHideItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(playPauseItem);
            contextMenu.Items.Add(prevItem);
            contextMenu.Items.Add(nextItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            
            // Initial update
            UpdateContextMenuItems();
        }

        private void UpdateContextMenuItems()
        {
            if (_notifyIcon.ContextMenuStrip == null) return;

            var loc = LocalizationService.Instance;
            var isVisible = Application.Current.MainWindow?.IsVisible ?? false;

            _showHideItem.Text = isVisible ? loc["MenuHideToTray"] : loc["MenuShowMainWindow"];

            // Update other items text
            var items = _notifyIcon.ContextMenuStrip.Items;
            
            // playPauseItem
            if (items[2] is ToolStripMenuItem playPauseItem)
            {
                playPauseItem.Text = _playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing ? loc["MenuPause"] : loc["MenuPlay"];
            }

            // prevItem
            if (items[3] is ToolStripMenuItem prevItem) prevItem.Text = loc["MenuPrevious"];

            // nextItem
            if (items[4] is ToolStripMenuItem nextItem) nextItem.Text = loc["MenuNext"];

            // exitItem
            if (items[6] is ToolStripMenuItem exitItem) exitItem.Text = loc["MenuExit"];
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
                ShowTrayControls();
            }
        }

        private void ShowTrayControls()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_trayWindow == null)
                {
                    _trayWindow = _container.Resolve<UI.Views.TrayControlsWindow>();
                }

                _trayWindow.ShowAtTaskbar();
            });
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
