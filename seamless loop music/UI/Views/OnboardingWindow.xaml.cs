using System;
using System.Windows;
using Prism.Events;
using seamless_loop_music.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.Views
{
    public partial class OnboardingWindow : Window
    {
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;

        public OnboardingWindow(IPlayerService playerService, IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _playerService = playerService;
            _eventAggregator = eventAggregator;
        }

        private async void BtnChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new UI.FolderPicker();
            if (!dialog.ShowDialog(this) || string.IsNullOrWhiteSpace(dialog.ResultPath))
            {
                return;
            }

            var loc = LocalizationService.Instance;
            btnChooseFolder.IsEnabled = false;
            btnLater.IsEnabled = false;
            txtStatus.Text = loc["OnboardingStatusScanning"];

            try
            {
                _playerService.AddMusicFolder(dialog.ResultPath);
                await _playerService.ScanMusicFoldersAsync();

                _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Publish(new CategoryItem
                {
                    Id = -1,
                    Name = loc["PlaylistAll"],
                    Icon = "\U0001F3B6",
                    Type = CategoryType.Playlist
                });

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                seamless_loop_music.AppDialogService.Show(
                    string.Format(loc["OnboardingScanError"], ex.Message),
                    loc["OnboardingTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                btnChooseFolder.IsEnabled = true;
                btnLater.IsEnabled = true;
                txtStatus.Text = loc["OnboardingStatusReady"];
            }
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
