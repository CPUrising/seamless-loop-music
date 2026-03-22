using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave; // 为了识别 PlaybackState
using seamless_loop_music.Models;
using seamless_loop_music.Services; // 引入新晋音乐总监
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Input;
using seamless_loop_music.UI;
using System.Threading.Tasks;
using System.Windows.Data;

namespace seamless_loop_music
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 重构版：不仅负责貌美如花（UI），还把脏活累活全扔给了 PlayerService
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IPlayerService _playerService;
        
        // 数据集合已迁移到各组件的 ViewModel

        private string _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        private string _configPath;
        private string _settingsPath;
        // private string _playlistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlists.conf"); // 永久退役！
        
        private string _currentLang = "zh-CN";
        private string _lastLoadedFilePath = "";
        private string _lastPlaylistPath = ""; 
        private double _globalVolume = 80; 
        private List<string> _recentFolders = new List<string>();

        private bool _isUpdatingUI = false;
        private DateTime _lastSeekTime = DateTime.MinValue; 
        // 视图绑定已迁移

        public MainWindow(IPlayerService playerService)
        {
            // 0. 初始化数据目录与路径
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
            
            _configPath = Path.Combine(_dataDir, "loop_config.csv");
            _settingsPath = Path.Combine(_dataDir, "settings.conf");

            // 确保目录规范存在
            foreach (var dir in new[] { "UI/Views", "UI/ViewModels", "UI/Themes", "UI/Controls" })
            {
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);
                if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);
            }

            // 1. Load settings FIRST
            LoadSettings(); 
            
            InitializeComponent();
            
            // 2. 聘请音乐总监 (由 DI 容器提供)
            _playerService = playerService;

            // 3. 订阅总监的信号
            _playerService.OnTrackLoaded += OnTrackLoaded;
            _playerService.OnPlayStateChanged += state => Dispatcher.Invoke(() => UpdateButtons(state));
            _playerService.OnStatusMessage += msg => Dispatcher.Invoke(() => {
                lblStatus.Text = msg; // 汇报进度，让 cpu 大人知道我在努力工作
            });
            
            // 索引同步逻辑已由 PlaylistSidebar 处理
            
            LoadConfig(); // 仅负责迁移旧数据
            
            try {
                ApplyLanguage(); 
            } catch (Exception ex) {
                MessageBox.Show("Language Load Error: " + ex.Message);
            }

            UpdateAudioInfoText();
            UpdateButtons(PlaybackState.Stopped);
            
            // 数据同步已由各组件 ViewModel 处理

            // 自动加载上次播放的文件
            if (!string.IsNullOrEmpty(_lastLoadedFilePath) && File.Exists(_lastLoadedFilePath)) {
                try {
                    txtFilePath.Text = _lastLoadedFilePath;
                    _playerService.LoadTrack(_lastLoadedFilePath);
                } catch { }
            }

            // 初始化匹配参数滑块
            sldMatchWindow.Value = _playerService.MatchWindowSize;
            sldSearchRadius.Value = _playerService.MatchSearchRadius;
            UpdateMatchLabels();
        }

        private void OnTrackLoaded(MusicTrack track)
        {
            Dispatcher.Invoke(() => {
                UpdateAudioInfoText();

                _isUpdatingUI = true;
                
                // 更新显示
                txtFilePath.Text = track.FilePath; // 这里！要把新房子的门牌号挂上去
                txtLoopSample.Text = track.LoopStart.ToString();
                txtLoopEndSample.Text = track.LoopEnd.ToString();
                btnResetAB.IsEnabled = _playerService.IsABMode;
                lblStatus.Text = $"{LocalizationService.Instance["StatusPlaying"]}: {track.Title}";
                
                // 信息更新已通过 DataContext 同步

                _isUpdatingUI = false;
                UpdateSecLabels(); // 计算秒数显示
            });
        }

        private void UpdateButtons(PlaybackState state) {
            // 此逻辑已移动到子组件 ViewModel
        }

        // 侧边栏所有相关业务逻辑已迁移到 PlaylistSidebarViewModel

        private void btnPlayMode_Click(object sender, RoutedEventArgs e)
        {
            // 切换模式：SingleLoop -> ListLoop -> Shuffle -> SingleLoop
            var nextMode = (PlayMode)(((int)_playerService.CurrentMode + 1) % 3);
            _playerService.CurrentMode = nextMode;
            UpdateModeUI();
        }

        private void UpdateModeUI()
        {
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            string modeText = "";
            switch (_playerService.CurrentMode)
            {
                case PlayMode.SingleLoop: modeText = LocalizationService.Instance["ModeSingle"]; break;
                case PlayMode.ListLoop: modeText = LocalizationService.Instance["ModeList"]; break;
                case PlayMode.Shuffle: modeText = LocalizationService.Instance["ModeShuffle"]; break;
            }
            btnPlayMode.Content = modeText;
        }

        private void txtLoopLimit_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_playerService == null) return;
            if (int.TryParse(txtLoopLimit.Text, out int limit))
            {
                _playerService.LoopLimit = Math.Max(1, limit);
            }
        }

        private void ApplyLoopSettings() {
            if (_isUpdatingUI) return;
            if (long.TryParse(txtLoopSample.Text, out long start)) _playerService.SetLoopStart(start);
            if (long.TryParse(txtLoopEndSample.Text, out long end)) _playerService.SetLoopEnd(end);
            UpdateSecLabels();
        }

        private void btnApplyLoop_Click(object sender, RoutedEventArgs e) {
            ApplyLoopSettings();
            _playerService.SaveCurrentTrack(); // 立即保存
            
            if (long.TryParse(txtLoopEndSample.Text, out long end) && long.TryParse(txtLoopSample.Text, out long start)) {
                
                // 关键修复：如果 end 为 0，表示“文件末尾”，此时需要用总长度来计算跳转点
                long actualEnd = end;
                if (actualEnd <= 0)
                {
                     // 尝试获取总采样数
                     long totalSamples = (long)(_playerService.TotalTime.TotalSeconds * _playerService.SampleRate);
                     if (totalSamples > 0) actualEnd = totalSamples;
                }

                long previewOffset = _playerService.SampleRate * 3;
                long target = Math.Max(start, actualEnd - previewOffset); // 确保不早于 Start
                
                _playerService.SeekToSample(target);
                _playerService.Play(); 
            }
        }

        private void btnSmartMatch_Click(object sender, RoutedEventArgs e) {
            // Match Start (Reverse): Fix End, Find Start
            ApplyLoopSettings();
            btnSmartMatch.IsEnabled = false;

            // 保持兼容，调用的是 Reverse 逻辑
            _playerService.SmartMatchLoopReverseAsync(() => {
                Dispatcher.Invoke(() => {
                    UpdateLoopUI();
                    btnSmartMatch.IsEnabled = true; 
                });
            });
        }

        private void btnSmartMatchForward_Click(object sender, RoutedEventArgs e) {
            // Match End (Forward): Fix Start, Find End
            ApplyLoopSettings();
            btnSmartMatchForward.IsEnabled = false;

            _playerService.SmartMatchLoopForwardAsync(() => {
                Dispatcher.Invoke(() => {
                    UpdateLoopUI();
                    btnSmartMatchForward.IsEnabled = true; 
                });
            });
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
                                         
            return false; // Not ready, user must install manually
        }

        private async void btnPyList_Click(object sender, RoutedEventArgs e)
        {
             ApplyLoopSettings();
             bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
             
             btnPyList.IsEnabled = false;

             if (!await EnsurePyMusicLooperReadyAsync())
             {
                 btnPyList.IsEnabled = true;
                 return;
             }

             lblStatus.Text = isZh ? "正在计算前10个循环点..." : "Fetching top 10 loops...";
             
             try 
             {
                 var candidates = await _playerService.GetLoopCandidatesAsync();
                 if (candidates.Count == 0)
                 {
                     MessageBox.Show(isZh ? "未找到循环点。" : "No loops found.");
                 }
                 else
                 {
                                           var win = new seamless_loop_music.UI.LoopListWindow(candidates, _playerService, EnsurePyMusicLooperReadyAsync);

                     win.Owner = this;
                     win.Show(); // 非模态窗口，允许用户一边看一边操作主界面（虽然 apply 会自动操作）
                 }
             }
             finally
             {
                 btnPyList.IsEnabled = true;
                 lblStatus.Text = isZh ? "就绪" : "Ready";
             }
        }

        private void btnResetAB_Click(object sender, RoutedEventArgs e)
        {
            _playerService.ResetABLoopPoints();
            UpdateLoopUI();
        }

        private void UpdateLoopUI()
        {
            txtLoopSample.Text = _playerService.LoopStartSample.ToString();
            txtLoopEndSample.Text = _playerService.LoopEndSample.ToString();
            
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            lblStatus.Text = isZh ? "智能匹配完成" : "Smart Match Done";
        }

        private void Adjust_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;

            string tag = btn.Tag.ToString();
            var parts = tag.Split(':');
            if (parts.Length < 2) return;

            string type = parts[0];   // Start / End
            string value = parts[1];  // Min / Max / number

            TextBox target = (type == "Start") ? txtLoopSample : txtLoopEndSample;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            
            long current = 0;
            long.TryParse(target.Text, out current);

            if (value == "Min")
            {
                target.Text = "0";
            }
            else if (value == "Max")
            {
                target.Text = total.ToString();
            }
            else
            {
                if (double.TryParse(value, out double deltaSec))
                {
                    // 使用 Service 中的 SampleRate 来计算偏移量
                    long delta = (long)(_playerService.SampleRate * deltaSec);
                    target.Text = Math.Max(0, Math.Min(total, current + delta)).ToString();
                }
            }
        }

        private void txtLoopSample_TextChanged(object sender, TextChangedEventArgs e) { 
            if (_playerService == null || _isUpdatingUI) return;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (long.TryParse(txtLoopSample.Text, out long val)) {
                if (val < 0) {
                    _isUpdatingUI = true;
                    txtLoopSample.Text = "0";
                    _isUpdatingUI = false;
                }
                else if (val > total && total > 0) {
                    _isUpdatingUI = true;
                    txtLoopSample.Text = total.ToString();
                    _isUpdatingUI = false;
                }
            }
            UpdateSecLabels(); 
        }
        
        private void txtLoopEndSample_TextChanged(object sender, TextChangedEventArgs e) {
            if (_playerService == null || _isUpdatingUI) return;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (long.TryParse(txtLoopEndSample.Text, out long val)) {
                if (val > total && total > 0) {
                    _isUpdatingUI = true;
                    txtLoopEndSample.Text = total.ToString();
                    _isUpdatingUI = false;
                }
                else if (val < 0) {
                    _isUpdatingUI = true;
                    txtLoopEndSample.Text = "0";
                    _isUpdatingUI = false;
                }
            }
            UpdateSecLabels();
        }

        private void UpdateSecLabels() {
            if (_playerService == null || _isUpdatingUI) return;
            _isUpdatingUI = true;
            int rate = _playerService.SampleRate;
            if (rate <= 0) rate = 44100;

            if (txtLoopStartSec != null && long.TryParse(txtLoopSample.Text, out long s)) 
                txtLoopStartSec.Text = ((double)s / rate).ToString("F3");
            if (txtLoopEndSec != null && long.TryParse(txtLoopEndSample.Text, out long end)) 
                txtLoopEndSec.Text = ((double)end / rate).ToString("F3");
            _isUpdatingUI = false;
        }

        private void txtLoopStartSec_TextChanged(object sender, TextChangedEventArgs e) {
            if (_playerService == null || _isUpdatingUI) return;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (double.TryParse(txtLoopStartSec.Text, out double sec)) {
                _isUpdatingUI = true;
                txtLoopSample.Text = ((long)Math.Max(0, Math.Min(total, sec * _playerService.SampleRate))).ToString();
                _isUpdatingUI = false;
            }
        }

        private void txtLoopEndSec_TextChanged(object sender, TextChangedEventArgs e) {
            if (_playerService == null || _isUpdatingUI) return;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (double.TryParse(txtLoopEndSec.Text, out double sec)) {
                _isUpdatingUI = true;
                txtLoopEndSample.Text = ((long)Math.Max(0, Math.Min(total, sec * _playerService.SampleRate))).ToString();
                _isUpdatingUI = false;
            }
        }

        // 进度更新已迁移到 PlaybackControlBarViewModel

        // 进度拖动已迁移到 PlaybackControlBarViewModel

        private void sldMatchParams_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_playerService == null || _isUpdatingUI) return;

            if (sldMatchWindow != null) _playerService.MatchWindowSize = sldMatchWindow.Value;
            if (sldSearchRadius != null) _playerService.MatchSearchRadius = sldSearchRadius.Value;

            UpdateMatchLabels();
            SaveSettings(); // 实时保存参数
        }

        private void UpdateMatchLabels()
        {
            if (lblMatchWindow != null) 
                lblMatchWindow.Text = string.Format(LocalizationService.Instance["LabelMatchWindow"], sldMatchWindow.Value);
            if (lblSearchRadius != null) 
                lblSearchRadius.Text = string.Format(LocalizationService.Instance["LabelSearchRadius"], sldSearchRadius.Value);
        }

        // 音量调节已迁移到 PlaybackControlBarViewModel

        private void btnSwitchLang_Click(object sender, RoutedEventArgs e) {
            _currentLang = (_currentLang == "en-US") ? "zh-CN" : "en-US";
            LocalizationService.Instance.CurrentCulture = new System.Globalization.CultureInfo(_currentLang);
            
            ApplyLanguage();
            SaveSettings();
        }

        private void LoadConfig() {
            try {
                if (File.Exists(_configPath)) {
                    MigrateCsvToSqlite();
                }
            } catch (Exception ex) {
                MessageBox.Show("Load Config Error: " + ex.Message);
            }
        }

        private void MigrateCsvToSqlite() {
            try {
                var lines = File.ReadAllLines(_configPath);
                var tracksToImport = new List<MusicTrack>();
                
                for (int i = 1; i < lines.Length; i++) {
                    var p = lines[i].Split('|');
                    if (p.Length >= 4 && long.TryParse(p[1], out long total)) {
                        string absPath = p[0];
                        tracksToImport.Add(new MusicTrack {
                            FileName = Path.GetFileName(absPath),
                            FilePath = absPath,
                            LoopStart = long.TryParse(p[2], out long s) ? s : 0,
                            LoopEnd = long.TryParse(p[3], out long e) ? e : total,
                            TotalSamples = total,
                            DisplayName = Path.GetFileNameWithoutExtension(absPath)
                        });
                    }
                }
                
                if (tracksToImport.Count > 0) {
                    _playerService.ImportTracks(tracksToImport);
                    
                    string bakPath = _configPath + ".bak";
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Move(_configPath, bakPath);
                    
                    bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
                    MessageBox.Show(isZh ? $"成功从旧配置文件导入 {tracksToImport.Count} 条数据！" : $"Imported {tracksToImport.Count} entries from CSV!");
                }
            } catch {}
        }

        // 仅在关闭时保存最后的配置（UI 状态），LoopConfig 已经通过 Service 实时保存了
        private void SaveConfig() { }

        private void LoadSettings() {
            try {
                if (!File.Exists(_settingsPath)) { 
                    _currentLang = System.Globalization.CultureInfo.InstalledUICulture.Name.StartsWith("zh") ? "zh-CN" : "en-US"; 
                } else {
                    foreach (var l in File.ReadAllLines(_settingsPath)) {
                        if (l.StartsWith("Language=")) _currentLang = l.Substring(9).Trim();
                        if (l.StartsWith("RecentFolders=")) {
                            var paths = l.Substring("RecentFolders=".Length).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            _recentFolders = paths.Where(p => Directory.Exists(p)).ToList();
                        }
                        if (l.StartsWith("LastFile=")) _lastLoadedFilePath = l.Substring(9).Trim();
                        if (l.StartsWith("LastPlaylist=")) _lastPlaylistPath = l.Substring(13).Trim();
                        if (l.StartsWith("Volume=") && double.TryParse(l.Substring(7), out double vol)) _globalVolume = vol;
                        if (l.StartsWith("MatchWindow=") && double.TryParse(l.Substring(12), out double mw)) _playerService.MatchWindowSize = mw;
                        if (l.StartsWith("SearchRadius=") && double.TryParse(l.Substring(13), out double sr)) _playerService.MatchSearchRadius = sr;
                    }
                }
                
                try {
                    LocalizationService.Instance.CurrentCulture = new System.Globalization.CultureInfo(_currentLang);
                } catch {
                    LocalizationService.Instance.CurrentCulture = new System.Globalization.CultureInfo("en-US");
                }

            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"LoadSettings Error: {ex.Message}");
            }
        }

        private void SaveSettings() { 
            try { 
                var lines = new List<string> { 
                    $"Language={_currentLang}", 
                    $"RecentFolders={string.Join(";", _recentFolders)}" 
                };
                if (!string.IsNullOrEmpty(txtFilePath.Text) && File.Exists(txtFilePath.Text)) {
                    lines.Add($"LastFile={txtFilePath.Text}");
                }
                
                if (!string.IsNullOrEmpty(_lastPlaylistPath)) {
                    lines.Add($"LastPlaylist={_lastPlaylistPath}");
                }

                lines.Add($"Volume={_playerService.Volume * 100}");
                lines.Add($"MatchWindow={sldMatchWindow.Value}");
                lines.Add($"SearchRadius={sldSearchRadius.Value}");

                File.WriteAllLines(_settingsPath, lines); 
            } catch (Exception ex) {
                 System.Diagnostics.Debug.WriteLine($"SaveSettings Error: {ex.Message}");
            } 
        }

        private void ApplyLanguage() {
            // Most of the localization is now handled by XAML bindings.
            // Only dynamic elements like status messages need to be updated here if they don't have bindings.
            
            UpdateModeUI(); // Update play mode button text
            UpdateAudioInfoText();
            UpdateButtons(_playerService?.PlaybackState ?? PlaybackState.Stopped);
            UpdateMatchLabels(); // 莱芙也要记得更新这里喵！

            // Refresh status if it's a simple state or includes variable parts
            if (lblStatus.Text == "Ready" || lblStatus.Text == "就绪")
                lblStatus.Text = LocalizationService.Instance["StatusReady"];
            else if (lblStatus.Text == "Smart Match Done" || lblStatus.Text == "智能匹配完成" || lblStatus.Text == "Done.")
                lblStatus.Text = LocalizationService.Instance["StatusDone"];
            else if (lblStatus.Text == "Paused" || lblStatus.Text == "播放暂停")
                lblStatus.Text = LocalizationService.Instance["StatusPaused"];
            else if (lblStatus.Text.Contains("Playing:") || lblStatus.Text.Contains("正在播放:"))
            {
                 string trackName = "";
                 if (lblStatus.Text.Contains(":"))
                    trackName = lblStatus.Text.Substring(lblStatus.Text.IndexOf(':') + 1).Trim();
                 lblStatus.Text = $"{LocalizationService.Instance["StatusPlaying"]}: {trackName}";
            }

            // Refresh localization if needed
        }

        private void UpdateAudioInfoText() {
            if (lblAudioInfo == null) return;
            bool isZh = LocalizationService.Instance.CurrentCulture.Name.StartsWith("zh");
            
            var track = _playerService?.CurrentTrack;
            long total = track?.TotalSamples ?? 0;
            int rate = _playerService?.SampleRate ?? 44100;

            if (total == 0) {
                 lblAudioInfo.Text = LocalizationService.Instance["AudioInfoInit"];
            } else {
                string info = isZh ? 
                    $"音频信息: {total} Samples | 采样率: {rate} Hz" : 
                    $"Audio Info: {total} Samples | Rate: {rate} Hz";

                // 如果有元数据，另起一行显示
                if (track != null && (!string.IsNullOrEmpty(track.Artist) || !string.IsNullOrEmpty(track.Album)))
                {
                    string metadata = "";
                    if (!string.IsNullOrEmpty(track.Artist)) metadata += (isZh ? "艺术家: " : "Artist: ") + track.Artist;
                    if (!string.IsNullOrEmpty(track.AlbumArtist) && track.AlbumArtist != track.Artist) 
                        metadata += " (" + (isZh ? "专辑艺术家: " : "Album Artist: ") + track.AlbumArtist + ")";
                    if (!string.IsNullOrEmpty(track.Album)) metadata += " | " + (isZh ? "专辑: " : "Album: ") + track.Album;
                    
                    info += "\n" + metadata;
                }

                lblAudioInfo.Text = info;
            }
        }

        protected override void OnClosed(EventArgs e) {
            // SaveConfig(); // 循环点实时存，这里不需要了
            // SavePlaylists(); // 数据库驱动，不需要文件保存了
            SaveSettings(); 
            _playerService?.Dispose();
            base.OnClosed(e);
        }
    }
}
