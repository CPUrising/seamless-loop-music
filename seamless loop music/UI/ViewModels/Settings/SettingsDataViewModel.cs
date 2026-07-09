using System.Windows;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Events;
using seamless_loop_music.Services;
using seamless_loop_music.Services.Sync;
using seamless_loop_music.Services.Sync.Models;
using seamless_loop_music.UI.Views.Settings;

namespace seamless_loop_music.UI.ViewModels.Settings
{
    public class SettingsDataViewModel : BindableBase
    {
        private readonly IPlayerService _playerService;
        private readonly IGitHubSyncService _githubSyncService;
        private readonly IGitHubSyncManagementService _gitHubSyncManagementService;
        private readonly IEventAggregator _eventAggregator;
        private bool _isSyncing;
        private string _gitHubOwner;
        private string _gitHubRepository;
        private string _gitHubBranch = "main";
        private string _gitHubPath = "seamless-loop/sync.json";
        private string _gitHubToken;
        private string _lastGitHubSyncTime;
        private string _gitHubStatusText;
        private bool _isGitHubSyncing;
        private bool _isManagementBusy;
        private string _managementStatusText;
        private int _localSongCount;
        private int _localPlaylistCount;
        private int _localLoopPointCount;
        private int _localRatingCount;
        private int _cloudSongReferenceCount;
        private int _cloudPlaylistCount;
        private int _cloudLoopPointCount;
        private int _cloudRatingCount;
        private int _matchedCloudSongReferences;
        private int _missingCloudSongReferences;
        private string _cloudStatusText;
        private bool _clearLocalPlaylists;
        private bool _clearLocalLoopPoints;
        private bool _clearLocalRatings;

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

        public string GitHubOwner
        {
            get => _gitHubOwner;
            set => SetProperty(ref _gitHubOwner, value);
        }

        public string GitHubRepository
        {
            get => _gitHubRepository;
            set => SetProperty(ref _gitHubRepository, value);
        }

        public string GitHubBranch
        {
            get => _gitHubBranch;
            set => SetProperty(ref _gitHubBranch, value);
        }

        public string GitHubPath
        {
            get => _gitHubPath;
            set => SetProperty(ref _gitHubPath, value);
        }

        public string GitHubToken
        {
            get => _gitHubToken;
            set => SetProperty(ref _gitHubToken, value);
        }

        public string LastGitHubSyncTime
        {
            get => _lastGitHubSyncTime;
            set => SetProperty(ref _lastGitHubSyncTime, value);
        }

        public string GitHubStatusText
        {
            get => _gitHubStatusText;
            set => SetProperty(ref _gitHubStatusText, value);
        }

        public bool IsGitHubSyncing
        {
            get => _isGitHubSyncing;
            set
            {
                if (SetProperty(ref _isGitHubSyncing, value))
                {
                    RaisePropertyChanged(nameof(GitHubSyncButtonText));
                    GitHubSyncNowCommand.RaiseCanExecuteChanged();
                    SaveGitHubConfigCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string GitHubSyncButtonText => IsGitHubSyncing ? "正在同步 GitHub..." : "立即同步";

        public bool IsManagementBusy
        {
            get => _isManagementBusy;
            set
            {
                if (SetProperty(ref _isManagementBusy, value))
                {
                    RaisePropertyChanged(nameof(RefreshOverviewButtonText));
                    RefreshOverviewCommand.RaiseCanExecuteChanged();
                    ForcePushLocalToCloudCommand.RaiseCanExecuteChanged();
                    DeleteCloudSnapshotCommand.RaiseCanExecuteChanged();
                    ClearLocalSyncDataCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ManagementStatusText
        {
            get => _managementStatusText;
            set => SetProperty(ref _managementStatusText, value);
        }

        public int LocalSongCount
        {
            get => _localSongCount;
            set => SetProperty(ref _localSongCount, value);
        }

        public int LocalPlaylistCount
        {
            get => _localPlaylistCount;
            set => SetProperty(ref _localPlaylistCount, value);
        }

        public int LocalLoopPointCount
        {
            get => _localLoopPointCount;
            set => SetProperty(ref _localLoopPointCount, value);
        }

        public int LocalRatingCount
        {
            get => _localRatingCount;
            set => SetProperty(ref _localRatingCount, value);
        }

        public int CloudSongReferenceCount
        {
            get => _cloudSongReferenceCount;
            set => SetProperty(ref _cloudSongReferenceCount, value);
        }

        public int CloudPlaylistCount
        {
            get => _cloudPlaylistCount;
            set => SetProperty(ref _cloudPlaylistCount, value);
        }

        public int CloudLoopPointCount
        {
            get => _cloudLoopPointCount;
            set => SetProperty(ref _cloudLoopPointCount, value);
        }

        public int CloudRatingCount
        {
            get => _cloudRatingCount;
            set => SetProperty(ref _cloudRatingCount, value);
        }

        public int MatchedCloudSongReferences
        {
            get => _matchedCloudSongReferences;
            set => SetProperty(ref _matchedCloudSongReferences, value);
        }

        public int MissingCloudSongReferences
        {
            get => _missingCloudSongReferences;
            set => SetProperty(ref _missingCloudSongReferences, value);
        }

        public string CloudStatusText
        {
            get => _cloudStatusText;
            set => SetProperty(ref _cloudStatusText, value);
        }

        public bool ClearLocalPlaylists
        {
            get => _clearLocalPlaylists;
            set => SetProperty(ref _clearLocalPlaylists, value);
        }

        public bool ClearLocalLoopPoints
        {
            get => _clearLocalLoopPoints;
            set => SetProperty(ref _clearLocalLoopPoints, value);
        }

        public bool ClearLocalRatings
        {
            get => _clearLocalRatings;
            set => SetProperty(ref _clearLocalRatings, value);
        }

        public string RefreshOverviewButtonText => IsManagementBusy ? "正在刷新概览..." : "刷新数据概览";

        public DelegateCommand SyncCommand { get; }
        public DelegateCommand SaveGitHubConfigCommand { get; }
        public DelegateCommand GitHubSyncNowCommand { get; }
        public DelegateCommand RefreshOverviewCommand { get; }
        public DelegateCommand ForcePushLocalToCloudCommand { get; }
        public DelegateCommand DeleteCloudSnapshotCommand { get; }
        public DelegateCommand ClearLocalSyncDataCommand { get; }

        public SettingsDataViewModel(IPlayerService playerService, IGitHubSyncService githubSyncService, IGitHubSyncManagementService gitHubSyncManagementService, IEventAggregator eventAggregator)
        {
            _playerService = playerService;
            _githubSyncService = githubSyncService;
            _gitHubSyncManagementService = gitHubSyncManagementService;
            _eventAggregator = eventAggregator;

            LoadGitHubConfig();
            ManagementStatusText = "点击“刷新数据概览”查看本机与云端同步数据。";
            CloudStatusText = "尚未读取云端状态";

            SyncCommand = new DelegateCommand(OnSync, () => !IsSyncing);
            SaveGitHubConfigCommand = new DelegateCommand(OnSaveGitHubConfig, () => !IsGitHubSyncing);
            GitHubSyncNowCommand = new DelegateCommand(OnGitHubSyncNow, () => !IsGitHubSyncing);
            RefreshOverviewCommand = new DelegateCommand(OnRefreshOverview, () => !IsManagementBusy);
            ForcePushLocalToCloudCommand = new DelegateCommand(OnForcePushLocalToCloud, () => !IsManagementBusy);
            DeleteCloudSnapshotCommand = new DelegateCommand(OnDeleteCloudSnapshot, () => !IsManagementBusy);
            ClearLocalSyncDataCommand = new DelegateCommand(OnClearLocalSyncData, () => !IsManagementBusy);
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(_ => RaisePropertyChanged(nameof(SyncButtonText)), ThreadOption.UIThread);
        }

        private void LoadGitHubConfig()
        {
            var config = _githubSyncService.GetConfig();
            GitHubOwner = config.Owner ?? string.Empty;
            GitHubRepository = config.Repository ?? string.Empty;
            GitHubBranch = string.IsNullOrWhiteSpace(config.Branch) ? "main" : config.Branch;
            GitHubPath = string.IsNullOrWhiteSpace(config.Path) ? "seamless-loop/sync.json" : config.Path;
            GitHubToken = config.Token ?? string.Empty;
            LastGitHubSyncTime = _githubSyncService.GetLastSyncTimeDisplay();
            GitHubStatusText = "请先保存 GitHub 配置，然后再执行同步。";
        }

        private void OnSaveGitHubConfig()
        {
            _githubSyncService.SaveConfig(new GitHubSyncConfig
            {
                Owner = GitHubOwner,
                Repository = GitHubRepository,
                Branch = string.IsNullOrWhiteSpace(GitHubBranch) ? "main" : GitHubBranch,
                Path = string.IsNullOrWhiteSpace(GitHubPath) ? "seamless-loop/sync.json" : GitHubPath,
                Token = GitHubToken
            });

            LastGitHubSyncTime = _githubSyncService.GetLastSyncTimeDisplay();
            GitHubStatusText = "配置已保存";
        }

        private async void OnGitHubSyncNow()
        {
            IsGitHubSyncing = true;
            GitHubStatusText = "正在与 GitHub 同步...";

            try
            {
                var report = await _githubSyncService.SyncNowAsync();

                if (report.Success)
                {
                    _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                    LastGitHubSyncTime = _githubSyncService.GetLastSyncTimeDisplay();
                    GitHubStatusText = BuildGitHubStatusSummary(report);
                }
                else
                {
                    GitHubStatusText = !string.IsNullOrWhiteSpace(report.ErrorMessage)
                        ? report.ErrorMessage
                        : report.Status;
                }
            }
            catch (System.Exception ex)
            {
                GitHubStatusText = ex.Message;
            }
            finally
            {
                IsGitHubSyncing = false;
            }
        }

        private static string BuildGitHubStatusSummary(GitHubSyncReport report)
        {
            return $"同步完成：状态 {report.Status}，下载 {report.Downloaded}，应用 {report.Applied}，上传 {report.Uploaded}";
        }

        private async void OnRefreshOverview()
        {
            await RefreshOverviewAsync();
        }

        private async System.Threading.Tasks.Task RefreshOverviewAsync()
        {
            IsManagementBusy = true;
            ManagementStatusText = "正在刷新同步数据概览...";

            try
            {
                var overview = await _gitHubSyncManagementService.RefreshOverviewAsync();
                ApplyOverview(overview);
            }
            catch (System.Exception ex)
            {
                ManagementStatusText = $"刷新失败：{ex.Message}";
                CloudStatusText = "读取失败";
            }
            finally
            {
                IsManagementBusy = false;
            }
        }

        private async void OnForcePushLocalToCloud()
        {
            var confirmed = seamless_loop_music.AppDialogService.Show(
                "远端 sync.json 会被本机数据替换，不会删除本机数据。\n\n确定继续吗？",
                "确认覆盖云端",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmed != MessageBoxResult.OK)
                return;

            IsManagementBusy = true;
            ManagementStatusText = "正在用本机数据覆盖云端...";

            try
            {
                var result = await _gitHubSyncManagementService.ForcePushLocalToCloudAsync();
                ManagementStatusText = BuildManagementOperationStatus(result, "已用本机数据覆盖云端。", "覆盖云端失败");
                if (result.Success)
                {
                    LastGitHubSyncTime = _githubSyncService.GetLastSyncTimeDisplay();
                    await RefreshOverviewAsync();
                }
            }
            catch (System.Exception ex)
            {
                ManagementStatusText = $"覆盖云端失败：{ex.Message}";
            }
            finally
            {
                IsManagementBusy = false;
            }
        }

        private async void OnDeleteCloudSnapshot()
        {
            var confirmed = seamless_loop_music.AppDialogService.Show(
                "只删除云端 sync.json，不删除本机数据。\n\n确定继续吗？",
                "确认删除云端同步文件",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmed != MessageBoxResult.OK)
                return;

            IsManagementBusy = true;
            ManagementStatusText = "正在删除云端同步文件...";

            try
            {
                var result = await _gitHubSyncManagementService.DeleteCloudSnapshotAsync();
                ManagementStatusText = BuildManagementOperationStatus(result, "云端同步文件已删除。", "删除云端同步文件失败");
                if (result.Success)
                {
                    LastGitHubSyncTime = _githubSyncService.GetLastSyncTimeDisplay();
                    await RefreshOverviewAsync();
                }
            }
            catch (System.Exception ex)
            {
                ManagementStatusText = $"删除云端同步文件失败：{ex.Message}";
            }
            finally
            {
                IsManagementBusy = false;
            }
        }

        private async void OnClearLocalSyncData()
        {
            var dialog = new ClearLocalSyncDataDialog(ClearLocalPlaylists, ClearLocalLoopPoints, ClearLocalRatings)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dialog.ShowDialog() != true)
                return;

            ClearLocalPlaylists = dialog.ClearPlaylists;
            ClearLocalLoopPoints = dialog.ClearLoopPoints;
            ClearLocalRatings = dialog.ClearRatings;

            if (!dialog.ClearPlaylists && !dialog.ClearLoopPoints && !dialog.ClearRatings)
            {
                ManagementStatusText = "请至少选择一项要清除的本机同步数据。";
                return;
            }

            IsManagementBusy = true;
            ManagementStatusText = "正在清除本机同步数据...";

            try
            {
                var result = await _gitHubSyncManagementService.ClearLocalSyncDataAsync(new ClearLocalSyncDataSelection
                {
                    ClearPlaylists = ClearLocalPlaylists,
                    ClearLoopPoints = ClearLocalLoopPoints,
                    ClearRatings = ClearLocalRatings
                });

                ManagementStatusText = result.Success
                    ? $"本机同步数据已清除，受影响项约 {result.AffectedCount} 条。"
                    : BuildManagementOperationStatus(result, string.Empty, "清除本机同步数据失败");

                if (result.Success)
                {
                    _eventAggregator.GetEvent<LibraryRefreshedEvent>().Publish();
                    await RefreshOverviewAsync();
                }
            }
            catch (System.Exception ex)
            {
                ManagementStatusText = $"清除本机同步数据失败：{ex.Message}";
            }
            finally
            {
                IsManagementBusy = false;
            }
        }

        private void ApplyOverview(SyncDataOverview overview)
        {
            LocalSongCount = overview.Local?.SongCount ?? 0;
            LocalPlaylistCount = overview.Local?.PlaylistCount ?? 0;
            LocalLoopPointCount = overview.Local?.LoopPointCount ?? 0;
            LocalRatingCount = overview.Local?.RatingCount ?? 0;

            CloudSongReferenceCount = overview.Cloud?.SongReferenceCount ?? 0;
            CloudPlaylistCount = overview.Cloud?.PlaylistCount ?? 0;
            CloudLoopPointCount = overview.Cloud?.LoopPointCount ?? 0;
            CloudRatingCount = overview.Cloud?.RatingCount ?? 0;

            MatchedCloudSongReferences = overview.MatchedCloudSongReferences;
            MissingCloudSongReferences = overview.MissingCloudSongReferences;

            if (!string.IsNullOrWhiteSpace(overview.ErrorMessage))
            {
                CloudStatusText = $"云端读取失败：{overview.ErrorMessage}";
                ManagementStatusText = CloudStatusText;
                return;
            }

            if (overview.Status == "not_configured")
            {
                CloudStatusText = "GitHub 同步尚未配置完成";
                ManagementStatusText = "请先填写并保存 GitHub 配置，再刷新数据概览。";
                return;
            }

            CloudStatusText = overview.CloudExists ? "已检测到云端同步文件" : "云端同步文件不存在";
            ManagementStatusText = overview.CloudExists
                ? "数据概览已更新。"
                : "数据概览已更新：当前云端还没有 sync.json。";
        }

        private static string BuildManagementOperationStatus(SyncManagementOperationResult result, string successText, string failurePrefix)
        {
            if (result.Success)
                return successText;

            return !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? $"{failurePrefix}：{result.ErrorMessage}"
                : $"{failurePrefix}：{result.Status}";
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

                seamless_loop_music.AppDialogService.Show($"{LocalizationService.Instance["MsgSyncSuccess"]}\n\nTracks updated: {tracks}\nPlaylists synced: {playlists}",
                    "Data Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                seamless_loop_music.AppDialogService.Show($"Sync error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSyncing = false;
            }
        }
    }
}
