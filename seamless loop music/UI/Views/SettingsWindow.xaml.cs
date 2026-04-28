using System;
using System.Linq;
using System.Windows;
using Prism.Events;
using seamless_loop_music.Services;
using seamless_loop_music.Events;
using System.Globalization;

namespace seamless_loop_music.UI.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IAppStateService _appState;
        private bool _isInitializing = true;

        public SettingsWindow(IPlayerService playerService, IEventAggregator eventAggregator, IAppStateService appState)
        {
            InitializeComponent();
            _playerService = playerService;
            _eventAggregator = eventAggregator;
            _appState = appState;

            LoadFolders();
            InitializeLanguage();
            _isInitializing = false;
        }

        private void LoadFolders()
        {
            lstFolders.ItemsSource = _playerService.GetMusicFolders();
        }

        private void InitializeLanguage()
        {
            var loc = LocalizationService.Instance;
            cmbLanguage.ItemsSource = loc.SupportedCultures;
            
            // 选中当前语言
            var current = loc.CurrentCulture;
            cmbLanguage.SelectedItem = loc.SupportedCultures.FirstOrDefault(c => c.Name == current.Name) 
                                       ?? loc.SupportedCultures[0];
        }

        private async void CmbLanguage_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (cmbLanguage.SelectedItem is CultureInfo culture)
            {
                LocalizationService.Instance.CurrentCulture = culture;
                // 立即保存设置
                await _appState.SaveCurrentStateAsync();
            }
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationService.Instance["MusicFoldersHeader"],
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
            btnScan.Content = LocalizationService.Instance["BtnScanning"];
            try
            {
                await _playerService.ScanMusicFoldersAsync();
                _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                MessageBox.Show(LocalizationService.Instance["MsgScanCompleted"], 
                    LocalizationService.Instance["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnScan.IsEnabled = true;
                btnScan.Content = LocalizationService.Instance["BtnScan"];
            }
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db",
                Title = LocalizationService.Instance["DataManagementHeader"]
            };

            if (openFileDialog.ShowDialog() == true)
            {
                btnSync.IsEnabled = false;
                btnSync.Content = LocalizationService.Instance["BtnSyncing"];

                try
                {
                    var (tracks, playlists) = await _playerService.SyncDatabaseAsync(openFileDialog.FileName);
                    
                    _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();

                    MessageBox.Show($"{LocalizationService.Instance["MsgSyncSuccess"]}\n\nTracks updated: {tracks}\nPlaylists synced: {playlists}", 
                        "Data Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Sync error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btnSync.IsEnabled = true;
                    btnSync.Content = LocalizationService.Instance["BtnSync"];
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}