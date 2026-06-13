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
                ShowMainWindow();
            }
            else if (e.Button == MouseButtons.Right)
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

                if (_trayWindow.DataContext is UI.ViewModels.TrayControlsViewModel viewModel)
                {
                    viewModel.RefreshMenuState();
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
