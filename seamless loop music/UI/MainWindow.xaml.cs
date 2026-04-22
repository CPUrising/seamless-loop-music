using System.Windows;
using Prism.Ioc;
using seamless_loop_music.Services;
using seamless_loop_music.UI.Views;

namespace seamless_loop_music
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var container = ((Prism.Unity.PrismApplication)Application.Current).Container;
            var playerService = container.Resolve<IPlayerService>();
            var eventAggregator = container.Resolve<Prism.Events.IEventAggregator>();
            var settingsWindow = new SettingsWindow(playerService, eventAggregator) { Owner = this };
            settingsWindow.ShowDialog();
        }
    }
}
