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
            var loc = LocalizationService.Instance;
            var picked = new UI.FolderPicker().PickFolders(this);

            if (picked.Canceled)
            {
                return;
            }

            if (!string.IsNullOrEmpty(picked.ErrorMessage))
            {
                seamless_loop_music.AppDialogService.Show(
                    string.Format(loc["MsgFolderPickerError"], picked.ErrorMessage),
                    loc["OnboardingTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (picked.Folders.Count == 0)
            {
                ShowPickerProblem(picked, loc);
                return;
            }

            btnChooseFolder.IsEnabled = false;
            btnLater.IsEnabled = false;
            txtStatus.Text = loc["OnboardingStatusScanning"];

            try
            {
                var added = _playerService.AddMusicFolders(picked.Folders);

                if (added.Added.Count == 0 && picked.Unresolved.Count == 0)
                {
                    seamless_loop_music.AppDialogService.Show(
                        loc["MsgFolderAddNothing"],
                        loc["OnboardingTitle"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    btnChooseFolder.IsEnabled = true;
                    btnLater.IsEnabled = true;
                    txtStatus.Text = loc["OnboardingStatusReady"];
                    return;
                }

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

        private static void ShowPickerProblem(seamless_loop_music.UI.FolderPickerResult picked, LocalizationService loc)
        {
            if (picked.Unresolved.Count > 0)
            {
                seamless_loop_music.AppDialogService.Show(
                    string.Format(loc["MsgFolderUnsupported"], picked.Unresolved[0]),
                    loc["OnboardingTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                seamless_loop_music.AppDialogService.Show(
                    loc["MsgFolderAddNothing"],
                    loc["OnboardingTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
