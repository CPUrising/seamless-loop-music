using System.Windows;
using Prism.Ioc;
using seamless_loop_music.Services;
using seamless_loop_music.UI.Views;

namespace seamless_loop_music
{
    public partial class MainWindow : Window
    {
        private readonly IAppStateService _appState;
        private readonly INotifyIconService _notifyIconService;

        public MainWindow(ITaskbarService taskbarService, IAppStateService appState, INotifyIconService notifyIconService)
        {
            InitializeComponent();
            _appState = appState;
            _notifyIconService = notifyIconService;
            taskbarService.Initialize(this.MainTaskbarItemInfo);

            this.Closing += MainWindow_Closing;
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
