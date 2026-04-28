using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Events;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;

namespace seamless_loop_music.UI.ViewModels
{
    public class LoopWorkspaceViewModel : BindableBase
    {
        private readonly IPlaybackService _playbackService;
        private readonly ILoopAnalysisService _loopAnalysisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlayerService _playerService;
        private readonly TrackMetadataService _metadataService;

        private bool _isUpdatingInternally;
        private MusicTrack _currentTrack;

        public LoopWorkspaceViewModel(IPlaybackService playbackService, ILoopAnalysisService loopAnalysisService, IEventAggregator eventAggregator, IPlayerService playerService, TrackMetadataService metadataService)
        {
            _playbackService = playbackService;
            _loopAnalysisService = loopAnalysisService;
            _eventAggregator = eventAggregator;
            _playerService = playerService;
            _metadataService = metadataService;

            AdjustCommand = new DelegateCommand<string>(ExecuteAdjust);
            SmartMatchReverseCommand = new DelegateCommand(ExecuteSmartMatchReverse);
            SmartMatchForwardCommand = new DelegateCommand(ExecuteSmartMatchForward);
            ResetABCommand = new DelegateCommand(ExecuteResetAB);
            PyRankingCommand = new DelegateCommand(ExecutePyRanking);
            ApplyLoopCommand = new DelegateCommand(ExecuteApplyLoop);

            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(OnTrackLoaded);
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Subscribe(OnLoopPointsChanged);
            
            _eventAggregator.GetEvent<StatusMessageEvent>().Subscribe(msg => 
            {
                Application.Current.Dispatcher.Invoke(() => StatusMessage = msg);
            });
            
            if (_playbackService.CurrentTrack != null)
            {
                OnTrackLoaded(_playbackService.CurrentTrack);
            }
        }

        private string _filePath = "";
        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        private string _loopStartSample = "0";
        public string LoopStartSample
        {
            get => _loopStartSample;
            set
            {
                long total = _currentTrack?.TotalSamples ?? 0;
                if (!long.TryParse(value, out long val)) val = 0;
                val = Math.Max(0, Math.Min(total, val));

                long end = long.TryParse(LoopEndSample, out long e) ? e : total;
                bool pushedEnd = false;
                if (val > end)
                {
                    _loopEndSample = val.ToString();
                    pushedEnd = true;
                    RaisePropertyChanged(nameof(LoopEndSample));
                }

                string validated = val.ToString();
                if (_loopStartSample != validated)
                {
                    _loopStartSample = validated;
                    RaisePropertyChanged();
                    UpdateSecFromSamples(true, pushedEnd);
                }
                else if (value != validated)
                {
                    RaisePropertyChanged();
                }
            }
        }

        private string _loopEndSample = "0";
        public string LoopEndSample
        {
            get => _loopEndSample;
            set
            {
                long total = _currentTrack?.TotalSamples ?? 0;
                if (!long.TryParse(value, out long val)) val = total;
                val = Math.Max(0, Math.Min(total, val));

                long start = long.TryParse(LoopStartSample, out long s) ? s : 0;
                if (val < start) val = start;

                string validated = val.ToString();
                if (_loopEndSample != validated)
                {
                    _loopEndSample = validated;
                    RaisePropertyChanged();
                    UpdateSecFromSamples(false, true);
                }
                else if (value != validated)
                {
                    RaisePropertyChanged();
                }
            }
        }

        private string _loopStartSec = "0.000";
        public string LoopStartSec
        {
            get => _loopStartSec;
            set
            {
                int rate = GetCurrentRate();
                double totalSec = (_currentTrack?.TotalSamples ?? 0) / (double)rate;
                if (!double.TryParse(value, out double val)) val = 0;
                val = Math.Max(0, Math.Min(totalSec, val));

                string validated = val.ToString("F3");
                if (_loopStartSec != validated)
                {
                    _loopStartSec = validated;
                    RaisePropertyChanged();
                    UpdateSamplesFromSec(true, false);
                }
                else if (value != validated)
                {
                    RaisePropertyChanged();
                }
            }
        }

        private string _loopEndSec = "0.000";
        public string LoopEndSec
        {
            get => _loopEndSec;
            set
            {
                int rate = GetCurrentRate();
                double totalSec = (_currentTrack?.TotalSamples ?? 0) / (double)rate;
                if (!double.TryParse(value, out double val)) val = totalSec;
                val = Math.Max(0, Math.Min(totalSec, val));

                string validated = val.ToString("F3");
                if (_loopEndSec != validated)
                {
                    _loopEndSec = validated;
                    RaisePropertyChanged();
                    UpdateSamplesFromSec(false, true);
                }
                else if (value != validated)
                {
                    RaisePropertyChanged();
                }
            }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => Application.Current.Dispatcher.Invoke(() => SetProperty(ref _statusMessage, value));
        }

        private string _audioInfo = "";
        public string AudioInfo
        {
            get => _audioInfo;
            set => SetProperty(ref _audioInfo, value);
        }

        public bool IsABMode => _playbackService.IsABFusionLoaded;

        private double _matchWindowSize = 1.0;
        public double MatchWindowSize
        {
            get => _matchWindowSize;
            set => SetProperty(ref _matchWindowSize, value);
        }

        private double _searchRadius = 5.0;
        public double SearchRadius
        {
            get => _searchRadius;
            set => SetProperty(ref _searchRadius, value);
        }

        public string MatchWindowTitle => LocalizationService.Instance["MatchWindowTitle"];
        public string SearchRadiusTitle => LocalizationService.Instance["SearchRadiusTitle"];

        public DelegateCommand<string> AdjustCommand { get; }
        public DelegateCommand SmartMatchReverseCommand { get; }
        public DelegateCommand SmartMatchForwardCommand { get; }
        public DelegateCommand ResetABCommand { get; }
        public DelegateCommand PyRankingCommand { get; }
        public DelegateCommand ApplyLoopCommand { get; }

        private void OnTrackLoaded(MusicTrack track)
        {
            _currentTrack = track;
            UpdateAudioInfo(track);
            LoopStartSample = track.LoopStart.ToString();
            LoopEndSample = track.LoopEnd.ToString();
            FilePath = track.FilePath;
            RaisePropertyChanged(nameof(IsABMode));
        }

        private void OnLoopPointsChanged((long start, long end) points)
        {
            _isUpdatingInternally = true;
            try
            {
                LoopStartSample = points.start.ToString();
                LoopEndSample = points.end.ToString();
                
                int rate = GetCurrentRate();
                if (rate > 0)
                {
                    LoopStartSec = ((double)points.start / rate).ToString("F3");
                    LoopEndSec = ((double)points.end / rate).ToString("F3");
                }
            }
            finally
            {
                _isUpdatingInternally = false;
            }
        }

        private void UpdateAudioInfo(MusicTrack track)
        {
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            long total = track?.TotalSamples ?? 0;
            
            int rate = (_playbackService.CurrentTrack?.Id == track?.Id)
                ? _playbackService.SampleRate
                : _metadataService.GetSampleRate(track?.FilePath);

            string info = isZh ? 
                $"音频信息: {total} Samples | 采样率: {rate} Hz" : 
                $"Audio Info: {total} Samples | Rate: {rate} Hz";
            
            AudioInfo = info;
        }

        private void ExecuteAdjust(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            string[] parts = parameter.Split(':');
            if (parts.Length < 2) return;

            string target = parts[0];
            string value = parts[1];

            long current = target == "Start" ? long.Parse(LoopStartSample) : long.Parse(LoopEndSample);
            long total = _currentTrack?.TotalSamples ?? 0;
            int rate = (_playbackService.CurrentTrack?.Id == _currentTrack?.Id)
                ? _playbackService.SampleRate
                : _metadataService.GetSampleRate(_currentTrack?.FilePath);

            long targetVal = current;
            if (value == "Min") targetVal = 0;
            else if (value == "Max") targetVal = total;
            else if (double.TryParse(value, out double deltaSec))
            {
                long delta = (long)(rate * deltaSec);
                targetVal = Math.Max(0, Math.Min(total, current + delta));
            }

            if (target == "Start") LoopStartSample = targetVal.ToString();
            else LoopEndSample = targetVal.ToString();
        }

        private async void ExecuteSmartMatchReverse()
        {
            try
            {
                ApplyLocalSettingsToService();
                if (!long.TryParse(LoopStartSample, out long start) || !long.TryParse(LoopEndSample, out long end)) return;
                
                var result = await _playbackService.FindBestLoopPointsAsync(start, end, true);
                LoopStartSample = result.Start.ToString();
                LoopEndSample = result.End.ToString();
                StatusMessage = LocalizationService.Instance["StatusDone"];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartMatchReverse失败] {ex.Message}");
            }
        }

        private async void ExecuteSmartMatchForward()
        {
            try
            {
                ApplyLocalSettingsToService();
                if (!long.TryParse(LoopStartSample, out long start) || !long.TryParse(LoopEndSample, out long end)) return;
                
                var result = await _playbackService.FindBestLoopPointsAsync(start, end, false);
                LoopStartSample = result.Start.ToString();
                LoopEndSample = result.End.ToString();
                StatusMessage = LocalizationService.Instance["StatusDone"];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartMatchForward失败] {ex.Message}");
            }
        }

        private void ExecuteResetAB()
        {
            _playerService.ResetABLoopPoints();
            LoopStartSample = _playerService.LoopStartSample.ToString();
            LoopEndSample = _playerService.LoopEndSample.ToString();
        }

        private async void ExecutePyRanking()
        {
            try
            {
                if (_playbackService.CurrentTrack == null) return;

                if (!await EnsurePyMusicLooperReadyAsync()) return;

                StatusMessage = LocalizationService.Instance["StatusSearching"];
                
                List<LoopCandidate> candidates = null;

                if (!string.IsNullOrEmpty(_playbackService.CurrentTrack.LoopCandidatesJson))
                {
                    candidates = _loopAnalysisService.DeserializeLoopCandidates(_playbackService.CurrentTrack.LoopCandidatesJson);
                }

                if (candidates == null || candidates.Count == 0)
                {
                    candidates = await _playerService.GetLoopCandidatesAsync();
                    
                    if (candidates != null && candidates.Count > 0)
                    {
                        await _playerService.UpdateTrackLoopCandidatesAsync(_currentTrack, candidates);
                    }
                }

                if (candidates == null || candidates.Count == 0)
                {
                    StatusMessage = LocalizationService.Instance["StatusNoCandidates"];
                    return;
                }

                StatusMessage = LocalizationService.Instance["StatusDone"];
                var win = new LoopListWindow(candidates, _playerService, EnsurePyMusicLooperReadyAsync);
                win.Owner = Application.Current.MainWindow;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PyRanking失败] {ex.Message}");
            }
        }

        private async Task<bool> EnsurePyMusicLooperReadyAsync()
        {
            int status = await _playerService.CheckPyMusicLooperStatusAsync();
            if (status == 0) return true;

            if (status == 2)
            {
                System.Windows.MessageBox.Show("PyMusicLooper 需要 uv 环境。请先安装 uv：\nhttps://github.com/astral-sh/uv",
                    "环境检查", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }

            System.Windows.MessageBox.Show("PyMusicLooper 未安装或需要下载。\n请运行: uvx pymusiclooper --version",
                "环境检查", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return false;
        }

        private async void ExecuteApplyLoop()
        {
            ApplyLocalSettingsToService();

            if (long.TryParse(LoopEndSample, out long end) && long.TryParse(LoopStartSample, out long start))
            {
                if (_playbackService.CurrentTrack?.Id != _currentTrack?.Id)
                {
                    await _playbackService.LoadTrackAsync(_currentTrack, false);
                }

                _playbackService.SetLoopPoints(start, end);
                
                int rate = _playbackService.SampleRate; 
                long previewOffset = rate * 3;
                long target = end - previewOffset;
                
                _playbackService.SeekToSample(target);
                _playbackService.Play();
            }
        }

        private void ApplyLocalSettingsToService()
        {
            _playerService.MatchWindowSize = MatchWindowSize;
            _playerService.MatchSearchRadius = SearchRadius;
            
            _playbackService.MatchWindowSize = MatchWindowSize;
            _playbackService.MatchSearchRadius = SearchRadius;
        }

        private int GetCurrentRate()
        {
            int rate = (_playbackService.CurrentTrack?.Id == _currentTrack?.Id)
                ? _playbackService.SampleRate
                : _metadataService.GetSampleRate(_currentTrack?.FilePath);
            
            return rate > 0 ? rate : 44100;
        }

        private void UpdateSecFromSamples(bool updateStart, bool updateEnd)
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            try
            {
                int rate = GetCurrentRate();

                if (updateStart && long.TryParse(LoopStartSample, out long s)) 
                    LoopStartSec = ((double)s / rate).ToString("F3");
                
                if (updateEnd && long.TryParse(LoopEndSample, out long e)) 
                    LoopEndSec = ((double)e / rate).ToString("F3");
            }
            finally
            {
                _isUpdatingInternally = false;
            }
        }

        private void UpdateSamplesFromSec(bool updateStart, bool updateEnd)
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            try
            {
                int rate = GetCurrentRate();
                long total = _currentTrack?.TotalSamples ?? 0;

                if (updateStart && double.TryParse(LoopStartSec, out double startSec))
                    LoopStartSample = ((long)Math.Max(0, Math.Min(total, startSec * rate))).ToString();

                if (updateEnd && double.TryParse(LoopEndSec, out double endSec))
                    LoopEndSample = ((long)Math.Max(0, Math.Min(total, endSec * rate))).ToString();
            }
            finally
            {
                _isUpdatingInternally = false;
            }
        }
    }
}
