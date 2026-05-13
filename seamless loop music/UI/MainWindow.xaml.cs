using System.Collections.Generic;
using System.Windows;
using Prism.Ioc;
using Prism.Events;
using seamless_loop_music.Events;
using seamless_loop_music.Services;
using seamless_loop_music.UI.Views;
using seamless_loop_music.Models;

namespace seamless_loop_music
{
    public partial class MainWindow : Window
    {
        private readonly IAppStateService _appState;
        private readonly INotifyIconService _notifyIconService;
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;

        public MainWindow(ITaskbarService taskbarService, 
            IAppStateService appState,
            INotifyIconService notifyIconService, 
            IPlayerService playerService,
            IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _appState = appState;
            _notifyIconService = notifyIconService;
            _playerService = playerService;
            _eventAggregator = eventAggregator;

            taskbarService.Initialize(this.MainTaskbarItemInfo);

            // 订阅事件以动态更新标题
            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(OnTrackLoaded, ThreadOption.UIThread);
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(OnLanguageChanged, ThreadOption.UIThread);

            UpdateTitle();

            this.Closing += MainWindow_Closing;
        }

        private void OnTrackLoaded(MusicTrack track)
        {
            UpdateTitle();
        }

        private void OnLanguageChanged(System.Globalization.CultureInfo culture)
        {
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            dynamic loc = this.FindResource("Loc");
            string appTitle = loc != null ? loc["AppTitle"] : LocalizationService.Instance["AppTitle"];

            if (_playerService.CurrentTrack != null && !string.IsNullOrEmpty(_playerService.CurrentTrack.Title))
            {
                this.Title = $"{_playerService.CurrentTrack.Title} - {appTitle}";
            }
            else
            {
                this.Title = appTitle;
            }
        }

        protected override void OnStateChanged(System.EventArgs e)
        {
            base.OnStateChanged(e);
            if (this.WindowState == WindowState.Minimized && _appState.MinimizeToTray)
            {
                _notifyIconService.HideMainWindow();
            }
        }

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_appState.IsExiting && _appState.CloseToTray)
            {
                e.Cancel = true;
                _notifyIconService.HideMainWindow();
                return;
            }

            await _appState.SaveCurrentStateAsync();
            _notifyIconService.Dispose();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var container = ((Prism.Unity.PrismApplication)Application.Current).Container;
            var playerService = container.Resolve<IPlayerService>();
            var eventAggregator = container.Resolve<Prism.Events.IEventAggregator>();
            var settingsWindow = new SettingsWindow(playerService, eventAggregator, _appState) { Owner = this };
            settingsWindow.ShowDialog();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
