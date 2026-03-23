using Prism.Commands;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using System;
using System.Windows;
using System.Windows.Threading;

namespace seamless_loop_music.UI.ViewModels
{
    public class LoopWorkspaceViewModel : BindableBase
    {
        private readonly IPlayerService _playerService;
        private bool _isUpdatingInternally = false;

        public LoopWorkspaceViewModel(IPlayerService playerService)
        {
            _playerService = playerService;

            // 命令初始化
            AdjustCommand = new DelegateCommand<string>(ExecuteAdjust);
            SmartMatchReverseCommand = new DelegateCommand(ExecuteSmartMatchReverse);
            SmartMatchForwardCommand = new DelegateCommand(ExecuteSmartMatchForward);
            ResetABCommand = new DelegateCommand(ExecuteResetAB, () => IsABMode).ObservesProperty(() => IsABMode);
            PyRankingCommand = new DelegateCommand(ExecutePyRanking);
            ApplyLoopCommand = new DelegateCommand(ExecuteApplyLoop);
            ChangePlayModeCommand = new DelegateCommand(ExecuteChangePlayMode);

            // 订阅服务事件
            _playerService.OnTrackLoaded += OnTrackLoaded;
            _playerService.OnStatusMessage += msg => StatusMessage = msg;
            _playerService.OnLoopPointsChanged += (start, end) => 
            {
                Application.Current.Dispatcher.Invoke(() => 
                {
                    _isUpdatingInternally = true;
                    LoopStartSample = start.ToString();
                    LoopEndSample = end.ToString();
                    _isUpdatingInternally = false;
                    UpdateSecLabels();
                });
            };

            // 初始值
            MatchWindowSize = _playerService.MatchWindowSize;
            SearchRadius = _playerService.MatchSearchRadius;
            UpdateModeText();

            // 检查初始状态：如果 Service 已经加载了音轨，手动触发一次同步
            if (_playerService.CurrentTrack != null)
            {
                OnTrackLoaded(_playerService.CurrentTrack);
            }
        }

        #region 属性

        private string _filePath = LocalizationService.Instance["NoFileSelected"];
        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        private string _statusMessage = LocalizationService.Instance["StatusReady"];
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _audioInfo = LocalizationService.Instance["AudioInfoInit"];
        public string AudioInfo
        {
            get => _audioInfo;
            set => SetProperty(ref _audioInfo, value);
        }

        private string _loopStartSample = "0";
        public string LoopStartSample
        {
            get => _loopStartSample;
            set
            {
                if (SetProperty(ref _loopStartSample, value))
                {
                    UpdateStartSecFromSample();
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
                    UpdateEndSecFromSample();
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
                    UpdateStartSampleFromSec();
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
                    UpdateEndSampleFromSec();
                }
            }
        }

        private int _loopLimit = 1;
        public int LoopLimit
        {
            get => _loopLimit;
            set
            {
                if (SetProperty(ref _loopLimit, value))
                {
                    _playerService.LoopLimit = Math.Max(1, value);
                }
            }
        }

        private string _playModeText;
        public string PlayModeText
        {
            get => _playModeText;
            set => SetProperty(ref _playModeText, value);
        }

        public bool IsABMode => _playerService.IsABMode;

        private double _matchWindowSize;
        public double MatchWindowSize
        {
            get => _matchWindowSize;
            set
            {
                if (SetProperty(ref _matchWindowSize, value))
                {
                    _playerService.MatchWindowSize = value;
                    RaisePropertyChanged(nameof(MatchWindowTitle));
                }
            }
        }

        private double _searchRadius;
        public double SearchRadius
        {
            get => _searchRadius;
            set
            {
                if (SetProperty(ref _searchRadius, value))
                {
                    _playerService.MatchSearchRadius = value;
                    RaisePropertyChanged(nameof(SearchRadiusTitle));
                }
            }
        }

        public string MatchWindowTitle => string.Format(LocalizationService.Instance["LabelMatchWindow"], MatchWindowSize);
        public string SearchRadiusTitle => string.Format(LocalizationService.Instance["LabelSearchRadius"], SearchRadius);

        #endregion

        #region 命令

        public DelegateCommand<string> AdjustCommand { get; }
        public DelegateCommand SmartMatchReverseCommand { get; }
        public DelegateCommand SmartMatchForwardCommand { get; }
        public DelegateCommand ResetABCommand { get; }
        public DelegateCommand PyRankingCommand { get; }
        public DelegateCommand ApplyLoopCommand { get; }
        public DelegateCommand ChangePlayModeCommand { get; }

        #endregion

        #region 方法实现

        private void OnTrackLoaded(MusicTrack track)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _isUpdatingInternally = true;
                
                FilePath = track.FilePath;
                LoopStartSample = track.LoopStart.ToString();
                LoopEndSample = track.LoopEnd.ToString();
                
                UpdateAudioInfo(track);
                RaisePropertyChanged(nameof(IsABMode));
                
                _isUpdatingInternally = false;
                UpdateSecLabels();
            });
        }

        private void UpdateAudioInfo(MusicTrack track)
        {
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            long total = track?.TotalSamples ?? 0;
            int rate = _playerService.SampleRate;

            string info = isZh ? 
                $"音频信息: {total} Samples | 采样率: {rate} Hz" : 
                $"Audio Info: {total} Samples | Rate: {rate} Hz";

            if (track != null && (!string.IsNullOrEmpty(track.Artist) || !string.IsNullOrEmpty(track.Album)))
            {
                string metadata = "";
                if (!string.IsNullOrEmpty(track.Artist)) metadata += (isZh ? "艺术家: " : "Artist: ") + track.Artist;
                if (!string.IsNullOrEmpty(track.AlbumArtist) && track.AlbumArtist != track.Artist) 
                    metadata += " (" + (isZh ? "专辑艺术家: " : "Album Artist: ") + track.AlbumArtist + ")";
                if (!string.IsNullOrEmpty(track.Album)) metadata += " | " + (isZh ? "专辑: " : "Album: ") + track.Album;
                info += "\n" + metadata;
            }
            AudioInfo = info;
        }

        private void ExecuteAdjust(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            var parts = tag.Split(':');
            if (parts.Length < 2) return;

            string type = parts[0];   // Start / End
            string value = parts[1];  // Min / Max / number

            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            long current;
            
            if (type == "Start") long.TryParse(LoopStartSample, out current);
            else long.TryParse(LoopEndSample, out current);

            long target = current;
            if (value == "Min") target = 0;
            else if (value == "Max") target = total;
            else if (double.TryParse(value, out double deltaSec))
            {
                long delta = (long)(_playerService.SampleRate * deltaSec);
                target = Math.Max(0, Math.Min(total, current + delta));
            }

            if (type == "Start") LoopStartSample = target.ToString();
            else LoopEndSample = target.ToString();
        }

        private void ExecuteSmartMatchReverse()
        {
            ApplyLocalSettingsToService();
            _playerService.SmartMatchLoopReverseAsync(() => 
            {
                Application.Current.Dispatcher.Invoke(() => 
                {
                    LoopStartSample = _playerService.LoopStartSample.ToString();
                    LoopEndSample = _playerService.LoopEndSample.ToString();
                    StatusMessage = LocalizationService.Instance["StatusDone"];
                });
            });
        }

        private void ExecuteSmartMatchForward()
        {
            ApplyLocalSettingsToService();
            _playerService.SmartMatchLoopForwardAsync(() => 
            {
                Application.Current.Dispatcher.Invoke(() => 
                {
                    LoopStartSample = _playerService.LoopStartSample.ToString();
                    LoopEndSample = _playerService.LoopEndSample.ToString();
                    StatusMessage = LocalizationService.Instance["StatusDone"];
                });
            });
        }

        private void ExecuteResetAB()
        {
            _playerService.ResetABLoopPoints();
            LoopStartSample = _playerService.LoopStartSample.ToString();
            LoopEndSample = _playerService.LoopEndSample.ToString();
        }

        private async System.Threading.Tasks.Task<bool> EnsurePyMusicLooperReadyAsync()
        {
            int status = await _playerService.CheckPyMusicLooperStatusAsync();
            if (status == 0) return true; // Ready

            if (status == 2)
            {
                MessageBox.Show(LocalizationService.Instance["MsgNoUv"], "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // status == 1, Needs manual setup
            MessageBox.Show(LocalizationService.Instance["PromptDownloadPymusiclooper"], 
                            LocalizationService.Instance["TitleDownloadNeeded"], 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                                         
            return false;
        }

        private async void ExecutePyRanking()
        {
            ApplyLocalSettingsToService();
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");

            if (!await EnsurePyMusicLooperReadyAsync()) return;

            StatusMessage = isZh ? "正在计算前10个循环点..." : "Fetching top 10 loops...";
            
            try 
            {
                var candidates = await _playerService.GetLoopCandidatesAsync();
                if (candidates.Count == 0)
                {
                    MessageBox.Show(isZh ? "未找到循环点。" : "No loops found.");
                }
                else
                {
                    // 在 ViewModel 中直接 New Window 虽不完美，但在当前重构阶段是最高效的迁移方式
                    Application.Current.Dispatcher.Invoke(() => {
                        var win = new LoopListWindow(candidates, _playerService, EnsurePyMusicLooperReadyAsync);
                        win.Owner = Application.Current.MainWindow;
                        win.Show();
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("PyRanking Error: " + ex.Message);
            }
            finally
            {
                StatusMessage = LocalizationService.Instance["StatusReady"];
            }
        }

        private void ExecuteApplyLoop()
        {
            ApplyLocalSettingsToService();
            _playerService.SaveCurrentTrack();

            if (long.TryParse(LoopEndSample, out long end) && long.TryParse(LoopStartSample, out long start))
            {
                long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
                long actualEnd = (end <= 0) ? total : end;
                long previewOffset = _playerService.SampleRate * 3;
                long target = Math.Max(start, actualEnd - previewOffset);
                
                _playerService.SeekToSample(target);
                _playerService.Play();
            }
        }

        private void ExecuteChangePlayMode()
        {
            var nextMode = (PlayMode)(((int)_playerService.CurrentMode + 1) % 3);
            _playerService.CurrentMode = nextMode;
            UpdateModeText();
        }

        private void UpdateModeText()
        {
            switch (_playerService.CurrentMode)
            {
                case PlayMode.SingleLoop: PlayModeText = LocalizationService.Instance["ModeSingle"]; break;
                case PlayMode.ListLoop: PlayModeText = LocalizationService.Instance["ModeList"]; break;
                case PlayMode.Shuffle: PlayModeText = LocalizationService.Instance["ModeShuffle"]; break;
            }
        }

        private void ApplyLocalSettingsToService()
        {
            if (long.TryParse(LoopStartSample, out long start)) _playerService.SetLoopStart(start);
            if (long.TryParse(LoopEndSample, out long end)) _playerService.SetLoopEnd(end);
        }

        private void UpdateSecLabels()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            int rate = _playerService.SampleRate > 0 ? _playerService.SampleRate : 44100;

            if (long.TryParse(LoopStartSample, out long s)) LoopStartSec = ((double)s / rate).ToString("F3");
            if (long.TryParse(LoopEndSample, out long e)) LoopEndSec = ((double)e / rate).ToString("F3");
            _isUpdatingInternally = false;
        }

        private void UpdateStartSecFromSample()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            int rate = _playerService.SampleRate > 0 ? _playerService.SampleRate : 44100;
            if (long.TryParse(LoopStartSample, out long s)) LoopStartSec = ((double)s / rate).ToString("F3");
            _isUpdatingInternally = false;
        }

        private void UpdateEndSecFromSample()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            int rate = _playerService.SampleRate > 0 ? _playerService.SampleRate : 44100;
            if (long.TryParse(LoopEndSample, out long e)) LoopEndSec = ((double)e / rate).ToString("F3");
            _isUpdatingInternally = false;
        }

        private void UpdateStartSampleFromSec()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (double.TryParse(LoopStartSec, out double sec))
                LoopStartSample = ((long)Math.Max(0, Math.Min(total, sec * _playerService.SampleRate))).ToString();
            _isUpdatingInternally = false;
        }

        private void UpdateEndSampleFromSec()
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (double.TryParse(LoopEndSec, out double sec))
                LoopEndSample = ((long)Math.Max(0, Math.Min(total, sec * _playerService.SampleRate))).ToString();
            _isUpdatingInternally = false;
        }

        #endregion
    }
}
