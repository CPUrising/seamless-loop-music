using System.Windows;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Events;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels.Settings
{
    public class SettingsDataViewModel : BindableBase
    {
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;
        private bool _isSyncing;

        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                if (SetProperty(ref _isSyncing, value))
                {
                    RaisePropertyChanged(nameof(SyncButtonText));
                    SyncCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string SyncButtonText => LocalizationService.Instance[IsSyncing ? "BtnSyncing" : "BtnSync"];

        public DelegateCommand SyncCommand { get; }

        public SettingsDataViewModel(IPlayerService playerService, IEventAggregator eventAggregator)
        {
            _playerService = playerService;
            _eventAggregator = eventAggregator;
            SyncCommand = new DelegateCommand(OnSync, () => !IsSyncing);
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(_ => RaisePropertyChanged(nameof(SyncButtonText)), ThreadOption.UIThread);
        }

        private async void OnSync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db",
                Title = LocalizationService.Instance["DataManagementHeader"]
            };

            if (openFileDialog.ShowDialog() != true) return;

            IsSyncing = true;
            try
            {
                var (tracks, playlists) = await _playerService.SyncDatabaseAsync(openFileDialog.FileName);
                _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();

                MessageBox.Show($"{LocalizationService.Instance["MsgSyncSuccess"]}\n\nTracks updated: {tracks}\nPlaylists synced: {playlists}",
                    "Data Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Sync error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSyncing = false;
            }
        }
    }
}
