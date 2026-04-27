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
        private readonly Prism.Ioc.IContainerProvider _container;
        private UI.Views.TrayControlsWindow _trayWindow;

        public NotifyIconService(IEventAggregator eventAggregator, IPlaybackService playbackService, Prism.Ioc.IContainerProvider container)
        {
            _eventAggregator = eventAggregator;
            _playbackService = playbackService;
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
            
            var playPauseItem = new ToolStripMenuItem("Play/Pause", null, (s, e) => {
                if (_playbackService.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                    _playbackService.Pause();
                else
                    _playbackService.Play();
            });

            var nextItem = new ToolStripMenuItem("Next", null, (s, e) => _playbackService.Next());
            var prevItem = new ToolStripMenuItem("Previous", null, (s, e) => _playbackService.Previous());
            
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Application.Current.Shutdown());

            contextMenu.Items.Add(playPauseItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(prevItem);
            contextMenu.Items.Add(nextItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
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
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;
                    
                    mainWindow.Activate();
                    mainWindow.Show();
                }
            });
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
