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
        private readonly IPlayerService _playerService; // Legacy for specific actions if needed

        private bool _isUpdatingInternally;

        public LoopWorkspaceViewModel(IPlaybackService playbackService, ILoopAnalysisService loopAnalysisService, IEventAggregator eventAggregator, IPlayerService playerService)
        {
            _playbackService = playbackService;
            _loopAnalysisService = loopAnalysisService;
            _eventAggregator = eventAggregator;
            _playerService = playerService;

            AdjustCommand = new DelegateCommand<string>(ExecuteAdjust);
            SmartMatchReverseCommand = new DelegateCommand(ExecuteSmartMatchReverse);
            SmartMatchForwardCommand = new DelegateCommand(ExecuteSmartMatchForward);
            ResetABCommand = new DelegateCommand(ExecuteResetAB);
            PyRankingCommand = new DelegateCommand(ExecutePyRanking);
            ApplyLoopCommand = new DelegateCommand(ExecuteApplyLoop);

            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(OnTrackLoaded);
            _eventAggregator.GetEvent<LoopPointsChangedEvent>().Subscribe(OnLoopPointsChanged);
        }

        private string _loopStartSample = "0";
        public string LoopStartSample
        {
            get => _loopStartSample;
            set
            {
                if (SetProperty(ref _loopStartSample, value))
                {
                    UpdateSecFromSamples();
                }
            }
        }

        private string _loopEndSample = "0";
        public string LoopEndSample
        {
            get => _loopEndSample;
            set
            {
                if (SetProperty(ref _loopEndSample, value))
                {
                    UpdateSecFromSamples();
                }
            }
        }

        private string _loopStartSec = "0.000";
        public string LoopStartSec
        {
            get => _loopStartSec;
            set
            {
                if (SetProperty(ref _loopStartSec, value))
                {
                    UpdateSamplesFromSecStart();
                }
            }
        }

        private string _loopEndSec = "0.000";
        public string LoopEndSec
        {
            get => _loopEndSec;
            set
            {
                if (SetProperty(ref _loopEndSec, value))
                {
                    UpdateSamplesFromSecEnd();
                }
            }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _audioInfo;
        public string AudioInfo
        {
            get => _audioInfo;
            set => SetProperty(ref _audioInfo, value);
        }

        private string _playModeText;
        public string PlayModeText
        {
            get => _playModeText;
            set => SetProperty(ref _playModeText, value);
        }

        public bool IsABMode => false; // TODO: Implement in PlaybackService

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

        public DelegateCommand<string> AdjustCommand { get; }
        public DelegateCommand SmartMatchReverseCommand { get; }
        public DelegateCommand SmartMatchForwardCommand { get; }
        public DelegateCommand ResetABCommand { get; }
        public DelegateCommand PyRankingCommand { get; }
        public DelegateCommand ApplyLoopCommand { get; }

        private void OnTrackLoaded(MusicTrack track)
        {
            UpdateAudioInfo(track);
            LoopStartSample = track.LoopStart.ToString();
            LoopEndSample = track.LoopEnd.ToString();
        }

        private void OnLoopPointsChanged((long start, long end) points)
        {
            _isUpdatingInternally = true;
            LoopStartSample = points.start.ToString();
            LoopEndSample = points.end.ToString();
            _isUpdatingInternally = false;
        }

        private void UpdateAudioInfo(MusicTrack track)
        {
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            long total = track?.TotalSamples ?? 0;
            int rate = _playbackService.SampleRate;

            string info = isZh ? 
                $"音频信息: {total} Samples | 采样率: {rate} Hz" : 
                $"Audio Info: {total} Samples | Rate: {rate} Hz";
            
            AudioInfo = info;
        }

        private void ExecuteAdjust(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            string[] parts = parameter.Split('|');
            if (parts.Length < 2) return;

            string target = parts[0];
            string value = parts[1];

            long current = target == "Start" ? long.Parse(LoopStartSample) : long.Parse(LoopEndSample);
            long total = _playbackService.CurrentTrack?.TotalSamples ?? 0;

            long targetVal = current;
            if (value == "Min") targetVal = 0;
            else if (value == "Max") targetVal = total;
            else if (double.TryParse(value, out double deltaSec))
            {
                long delta = (long)(_playbackService.SampleRate * deltaSec);
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
                var candidates = await _playerService.GetLoopCandidatesAsync();

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

        private void ExecuteApplyLoop()
        {
            ApplyLocalSettingsToService();

            if (long.TryParse(LoopEndSample, out long end) && long.TryParse(LoopStartSample, out long start))
            {
                _playbackService.SetLoopPoints(start, end);
                
                long previewOffset = _playbackService.SampleRate * 3;
                long target = end - previewOffset;
                
                _playbackService.SeekToSample(target);
                _playbackService.Play();
            }
        }

        private void ApplyLocalSettingsToService()
        {
            _playerService.MatchWindowSize = MatchWindowSize;
            _playerService.MatchSearchRadius = SearchRadius;
        }

        private void UpdateSecFromSamples()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            int rate = _playbackService.SampleRate > 0 ? _playbackService.SampleRate : 44100;
            if (long.TryParse(LoopStartSample, out long s)) LoopStartSec = ((double)s / rate).ToString("F3");
            if (long.TryParse(LoopEndSample, out long e)) LoopEndSec = ((double)e / rate).ToString("F3");
            _isUpdatingInternally = false;
        }

        private void UpdateSamplesFromSecStart()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            int rate = _playbackService.SampleRate > 0 ? _playbackService.SampleRate : 44100;
            long total = _playbackService.CurrentTrack?.TotalSamples ?? 0;
            if (double.TryParse(LoopStartSec, out double sec))
                LoopStartSample = ((long)Math.Max(0, Math.Min(total, sec * rate))).ToString();
            _isUpdatingInternally = false;
        }

        private void UpdateSamplesFromSecEnd()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            int rate = _playbackService.SampleRate > 0 ? _playbackService.SampleRate : 44100;
            long total = _playbackService.CurrentTrack?.TotalSamples ?? 0;
            if (double.TryParse(LoopEndSec, out double sec))
                LoopEndSample = ((long)Math.Max(0, Math.Min(total, sec * rate))).ToString();
            _isUpdatingInternally = false;
        }
    }
}
