using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Events;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.UI;

namespace seamless_loop_music.UI.ViewModels.Settings
{
    public class SettingsMusicViewModel : BindableBase
    {
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;
        private string _selectedFolder;
        private bool _isScanning;

        public ObservableCollection<string> MusicFolders { get; } = new ObservableCollection<string>();

        public string SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (SetProperty(ref _selectedFolder, value))
                {
                    RemoveFolderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    RaisePropertyChanged(nameof(ScanButtonText));
                    ScanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ScanButtonText => LocalizationService.Instance[IsScanning ? "BtnScanning" : "BtnScan"];

        public DelegateCommand AddFolderCommand { get; }
        public DelegateCommand RemoveFolderCommand { get; }
        public DelegateCommand ScanCommand { get; }

        public SettingsMusicViewModel(IPlayerService playerService, IEventAggregator eventAggregator)
        {
            _playerService = playerService;
            _eventAggregator = eventAggregator;

            AddFolderCommand = new DelegateCommand(OnAddFolder);
            RemoveFolderCommand = new DelegateCommand(OnRemoveFolder, () => !string.IsNullOrEmpty(SelectedFolder));
            ScanCommand = new DelegateCommand(OnScan, () => !IsScanning);

            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(_ => RaisePropertyChanged(nameof(ScanButtonText)), ThreadOption.UIThread);
            LoadFolders();
        }

        private void LoadFolders()
        {
            MusicFolders.Clear();
            foreach (var folder in _playerService.GetMusicFolders())
            {
                MusicFolders.Add(folder);
            }

            if (!MusicFolders.Contains(SelectedFolder))
            {
                SelectedFolder = MusicFolders.FirstOrDefault();
            }
        }

        private void OnAddFolder()
        {
            var loc = LocalizationService.Instance;
            var result = new FolderPicker().PickFolders(Application.Current.MainWindow);

            if (result.Canceled) return;

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                seamless_loop_music.AppDialogService.Show(
                    string.Format(loc["MsgFolderPickerError"], result.ErrorMessage),
                    loc["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (result.Folders.Count == 0)
            {
                ShowNothingAddedMessage(result, loc);
                return;
            }

            try
            {
                var added = _playerService.AddMusicFolders(result.Folders);
                LoadFolders();

                if (result.Unresolved.Count > 0)
                {
                    seamless_loop_music.AppDialogService.Show(
                        string.Format(loc["MsgFolderUnsupported"], result.Unresolved[0]),
                        loc["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (added.Added.Count == 0)
                {
                    seamless_loop_music.AppDialogService.Show(
                        loc["MsgFolderAddNothing"],
                        loc["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                seamless_loop_music.AppDialogService.Show(
                    string.Format(loc["MsgFolderAddError"], ex.Message),
                    loc["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ShowNothingAddedMessage(seamless_loop_music.UI.FolderPickerResult result, LocalizationService loc)
        {
            if (result.Unresolved.Count > 0)
            {
                seamless_loop_music.AppDialogService.Show(
                    string.Format(loc["MsgFolderUnsupported"], result.Unresolved[0]),
                    loc["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                seamless_loop_music.AppDialogService.Show(
                    loc["MsgFolderAddNothing"],
                    loc["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnRemoveFolder()
        {
            if (string.IsNullOrEmpty(SelectedFolder)) return;

            _playerService.RemoveMusicFolder(SelectedFolder);
            LoadFolders();
            SelectAllTracks();
        }

        private async void OnScan()
        {
            IsScanning = true;
            try
            {
                await _playerService.ScanMusicFoldersAsync();
                _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                SelectAllTracks();
                seamless_loop_music.AppDialogService.Show(LocalizationService.Instance["MsgScanCompleted"],
                    LocalizationService.Instance["SettingsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                seamless_loop_music.AppDialogService.Show($"Scan error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void SelectAllTracks()
        {
            var loc = LocalizationService.Instance;
            _eventAggregator.GetEvent<CategoryItemSelectedEvent>().Publish(new CategoryItem
            {
                Id = -1,
                Name = loc["PlaylistAll"],
                Icon = "\U0001F3B6",
                Type = CategoryType.Playlist
            });
        }
    }
}
