using System.Windows;
using Prism.Ioc;
using seamless_loop_music.Services;
using seamless_loop_music.UI.Views;

namespace seamless_loop_music
{
    public partial class MainWindow : Window
    {
        public MainWindow(ITaskbarService taskbarService)
        {
            InitializeComponent();
            taskbarService.Initialize(this.MainTaskbarItemInfo);

            this.Closing += MainWindow_Closing;
        }

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var container = ((Prism.Unity.PrismApplication)Application.Current).Container;
            var appState = container.Resolve<IAppStateService>();
            await appState.SaveCurrentStateAsync();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var container = ((Prism.Unity.PrismApplication)Application.Current).Container;
            var playerService = container.Resolve<IPlayerService>();
            var eventAggregator = container.Resolve<Prism.Events.IEventAggregator>();
            var appState = container.Resolve<IAppStateService>();
            var settingsWindow = new SettingsWindow(playerService, eventAggregator, appState) { Owner = this };
            settingsWindow.ShowDialog();
        }
    }
}
