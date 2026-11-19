using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Services.MusicClassification;

namespace seamless_loop_music.UI.ViewModels
{
    /// <summary>
    /// 音乐分类批量分析视图模型：选文件夹→后台批量分析→结果列表展示（仅展示不落库）。
    /// </summary>
    public class MusicClassificationViewModel : BindableBase
    {
        private readonly IMusicClassificationService _classificationService;
        private readonly IPlaybackService _playbackService;
        private readonly ITrackRepository _trackRepository;
        private CancellationTokenSource _cts;

        public ObservableCollection<TrackClassificationResult> Results { get; }
            = new ObservableCollection<TrackClassificationResult>();

        private string _folderPath;
        public string FolderPath
        {
            get => _folderPath;
            set => SetProperty(ref _folderPath, value);
        }

        private bool _isAnalyzing;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    AnalyzeCommand?.RaiseCanExecuteChanged();
                    CancelCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _statusText = "未开始";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private int _summaryIntense;
        public int SummaryIntense { get => _summaryIntense; set => SetProperty(ref _summaryIntense, value); }
        private int _summaryBright;
        public int SummaryBright { get => _summaryBright; set => SetProperty(ref _summaryBright, value); }
        private int _summaryCalm;
        public int SummaryCalm { get => _summaryCalm; set => SetProperty(ref _summaryCalm, value); }
        private int _summarySteady;
        public int SummarySteady { get => _summarySteady; set => SetProperty(ref _summarySteady, value); }

        public DelegateCommand BrowseCommand { get; }
        public DelegateCommand AnalyzeCommand { get; }
        public DelegateCommand CancelCommand { get; }

        /// <summary>双击结果行播放该曲目。</summary>
        public DelegateCommand<TrackClassificationResult> PlayCommand { get; }

        public MusicClassificationViewModel(
            IMusicClassificationService classificationService,
            IPlaybackService playbackService,
            ITrackRepository trackRepository)
        {
            _classificationService = classificationService;
            _playbackService = playbackService;
            _trackRepository = trackRepository;

            BrowseCommand = new DelegateCommand(OnBrowse, () => !IsAnalyzing);
            AnalyzeCommand = new DelegateCommand(async () => await OnAnalyzeAsync(),
                () => !IsAnalyzing && !string.IsNullOrWhiteSpace(FolderPath));
            CancelCommand = new DelegateCommand(() => _cts?.Cancel(), () => IsAnalyzing);
            PlayCommand = new DelegateCommand<TrackClassificationResult>(
                async r => await OnPlayAsync(r), r => r != null && r.Succeeded);

            if (classificationService is MusicClassificationService svc)
            {
                svc.OnStatusMessage += msg =>
                    Application.Current?.Dispatcher?.Invoke(() => StatusText = msg);
            }
        }

        private void OnBrowse()
        {
            var picker = new FolderPicker();
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current?.MainWindow;
            var result = picker.PickFolders(owner);
            if (result.Canceled || result.Folders.Count == 0) return;

            FolderPath = result.Folders[0];
            AnalyzeCommand.RaiseCanExecuteChanged();
        }

        private async Task OnAnalyzeAsync()
        {
            IsAnalyzing = true;
            BrowseCommand.RaiseCanExecuteChanged();
            Results.Clear();
            ClearSummary();
            ProgressValue = 0;
            _cts = new CancellationTokenSource();

            var progress = new Progress<(int current, int total, string fileName)>(p =>
            {
                StatusText = $"({p.current}/{p.total}) {p.fileName}";
                ProgressValue = p.total > 0 ? (100.0 * p.current / p.total) : 0;
            });

            try
            {
                var list = await _classificationService.ClassifyFolderAsync(
                    FolderPath, true, progress, _cts.Token);

                foreach (var r in list) Results.Add(r);
                UpdateSummary(list);
                StatusText = $"完成：共 {list.Count} 首，成功 {list.Count(x => x.Succeeded)} 首";
            }
            catch (OperationCanceledException)
            {
                StatusText = "已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"分析失败：{ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
                BrowseCommand.RaiseCanExecuteChanged();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// 双击播放：按文件路径解析出媒体库中的曲目（保证有真实 Id，避免外键写入失败），
        /// 未入库的文件直接提示，不静默失败。
        /// </summary>
        private async Task OnPlayAsync(TrackClassificationResult result)
        {
            if (result == null || !result.Succeeded) return;

            try
            {
                var track = await _trackRepository.GetByFilePathAsync(result.FilePath);
                if (track == null)
                {
                    StatusText = $"未在媒体库中找到：{result.FileName}（请先扫描该文件夹）";
                    return;
                }

                await _playbackService.LoadTrackAsync(track, true);
                StatusText = $"正在播放：{result.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"播放失败：{ex.Message}";
            }
        }

        private void ClearSummary()
        {
            SummaryIntense = SummaryBright = SummaryCalm = SummarySteady = 0;
        }

        private void UpdateSummary(System.Collections.Generic.List<TrackClassificationResult> list)
        {
            SummaryIntense = list.Count(x => x.Category == MusicCategory.Intense);
            SummaryBright = list.Count(x => x.Category == MusicCategory.Bright);
            SummaryCalm = list.Count(x => x.Category == MusicCategory.Calm);
            SummarySteady = list.Count(x => x.Category == MusicCategory.Steady);
        }
    }
}
