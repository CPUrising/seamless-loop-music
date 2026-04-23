using System;
using System.Windows;
using Prism.Events;
using seamless_loop_music.Services;
using seamless_loop_music.Events;

namespace seamless_loop_music.UI.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;

        public SettingsWindow(IPlayerService playerService, IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _playerService = playerService;
            _eventAggregator = eventAggregator;
            LoadFolders();
        }

        private void LoadFolders()
        {
            lstFolders.ItemsSource = _playerService.GetMusicFolders();
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Music Folder",
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _playerService.AddMusicFolder(dialog.SelectedPath);
                LoadFolders();
            }
        }

        private void BtnRemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (lstFolders.SelectedItem is string path)
            {
                _playerService.RemoveMusicFolder(path);
                LoadFolders();
            }
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            btnScan.IsEnabled = false;
            btnScan.Content = "Scanning...";
            try
            {
                await _playerService.ScanMusicFoldersAsync();
                _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                MessageBox.Show("Scan completed!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnScan.IsEnabled = true;
                btnScan.Content = "Scan Now";
            }
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db",
                Title = "Select Old LoopData.db File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                btnSync.IsEnabled = false;
                btnSync.Content = "Syncing...";

                try
                {
                    var (tracks, playlists) = await _playerService.SyncDatabaseAsync(openFileDialog.FileName);
                    
                    _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();

                    MessageBox.Show($"Sync completed successfully!\n\nTracks updated: {tracks}\nPlaylists synced: {playlists}", 
                        "Data Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Sync error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btnSync.IsEnabled = true;
                    btnSync.Content = "Sync from Old Database";
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}