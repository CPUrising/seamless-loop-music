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
            var playerService = ((Prism.Unity.PrismApplication)Application.Current).Container.Resolve<IPlayerService>();
            var settingsWindow = new SettingsWindow(playerService) { Owner = this };
            settingsWindow.ShowDialog();
        }
    }
}
