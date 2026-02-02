using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;

namespace seamless_loop_music
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AudioLooper _audioLooper;
        private List<string> _playlist = new List<string>();
        private int _currentTrackIndex = -1;

        private class LoopConfigItem { public long LoopStart; public long LoopEnd; }
        private class PlaylistFolder { public string Name { get; set; } public string Path { get; set; } }

        private Dictionary<string, LoopConfigItem> _loopConfigs = new Dictionary<string, LoopConfigItem>();
        private List<string> _recentFolders = new List<string>(); // Keep for backward compatibility or future use
        private List<PlaylistFolder> _playlists = new List<PlaylistFolder>();

        private string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loop_config.csv");
        private string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.conf");
        private string _playlistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlists.conf");
        private string _currentLang = "zh-CN";

        private string _currentConfigKey = null;
        private int _currentSampleRate = 44100;
        private long _totalSamples = 0;
        private bool _isUpdatingUI = false;
        private bool _isDraggingProgress = false;
        private DateTime _lastSeekTime = DateTime.MinValue; // 新增：记录最后一次跳转的时间
        private DispatcherTimer _tmrUpdate;

        public MainWindow()
        {
            InitializeComponent();
            _audioLooper = new AudioLooper();
            
            LoadSettings();
            LoadConfig();
            LoadPlaylists();
            ApplyLanguage();

            // 自动选中第一个歌单（如果有的话）
            if (lstPlaylists.Items.Count > 0) lstPlaylists.SelectedIndex = 0;

            // 监听音频加载
            _audioLooper.OnAudioLoaded += (total, rate) => Dispatcher.Invoke(() => {
                _currentSampleRate = rate;
                _totalSamples = total;
                string fileName = Path.GetFileName(txtFilePath.Text);
                _currentConfigKey = $"{fileName}_{total}";
                
                UpdateAudioInfoText();

                _isUpdatingUI = true;
                if (_loopConfigs.ContainsKey(_currentConfigKey)) {
                    var cfg = _loopConfigs[_currentConfigKey];
                    if (cfg.LoopEnd <= 0) cfg.LoopEnd = total;
                    _audioLooper.SetLoopStartSample(cfg.LoopStart);
                    _audioLooper.SetLoopEndSample(cfg.LoopEnd);
                    txtLoopSample.Text = cfg.LoopStart.ToString();
                    txtLoopEndSample.Text = cfg.LoopEnd.ToString();
                } else {
                    _audioLooper.SetLoopStartSample(0);
                    _audioLooper.SetLoopEndSample(total);
                    txtLoopSample.Text = "0";
                    txtLoopEndSample.Text = total.ToString();
                }
                _isUpdatingUI = false;
                ApplyLoopSettings();
            });

            // 监听播放状态
            _audioLooper.OnPlayStateChanged += (state) => Dispatcher.Invoke(() => UpdateButtons(state));

            // 初始化定时器
            _tmrUpdate = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tmrUpdate.Tick += (s, e) => {
                // 如果正在拖动，或者刚刚完成跳转（500ms内），则不刷新进度条
                if (_isDraggingProgress || (DateTime.Now - _lastSeekTime).TotalMilliseconds < 500) return;
                
                var current = _audioLooper.CurrentTime;
                var total = _audioLooper.TotalTime;
                lblTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
                if (total.TotalMilliseconds > 0) {
                    _isUpdatingUI = true; // 标记正在由程序更新UI，防止触发 ValueChanged 逻辑循环
                    trkProgress.Value = (current.TotalMilliseconds / total.TotalMilliseconds) * trkProgress.Maximum;
                    _isUpdatingUI = false;
                }
            };
        }

        private void UpdateButtons(PlaybackState state) {
            bool isZh = (_currentLang == "zh-CN");
            bool hasFile = !string.IsNullOrEmpty(txtFilePath.Text);
            btnPlay.IsEnabled = hasFile && (state != PlaybackState.Playing);
            btnPause.IsEnabled = hasFile && (state == PlaybackState.Playing);
            btnStop.IsEnabled = btnPrev.IsEnabled = btnNext.IsEnabled = hasFile;

            switch (state) {
                case PlaybackState.Playing: _tmrUpdate.Start(); lblStatus.Text = isZh ? "播放中..." : "Playing..."; break;
                case PlaybackState.Paused: _tmrUpdate.Stop(); lblStatus.Text = isZh ? "已暂停" : "Paused"; break;
                case PlaybackState.Stopped: _tmrUpdate.Stop(); lblStatus.Text = isZh ? "就绪" : "Ready"; trkProgress.Value = 0; lblTime.Text = "00:00 / 00:00"; break;
            }
        }

        private void btnAddPlaylist_Click(object sender, RoutedEventArgs e) {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                string folderPath = dialog.SelectedPath;
                string folderName = Path.GetFileName(folderPath);
                if (string.IsNullOrEmpty(folderName)) folderName = folderPath; // Handle root drives

                // 简单的重名处理或直接让用户输入（这里先用默认名）
                _playlists.Add(new PlaylistFolder { Name = folderName, Path = folderPath });
                UpdatePlaylistUI();
                SavePlaylists();
                
                // 自动选中新添加的
                lstPlaylists.SelectedIndex = _playlists.Count - 1;
            }
        }

        private void lstPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                LoadPlaylist(folder.Path);
            }
        }

        private void miRenamePlaylist_Click(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                // 这是一个简单的演示，实际可能需要一个输入对话框
                string newName = Microsoft.VisualBasic.Interaction.InputBox("输入新的歌单名称：", "重命名", folder.Name);
                if (!string.IsNullOrEmpty(newName)) {
                    folder.Name = newName;
                    UpdatePlaylistUI();
                    SavePlaylists();
                }
            }
        }

        private void miDeletePlaylist_Click(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                _playlists.Remove(folder);
                UpdatePlaylistUI();
                SavePlaylists();
                lstPlaylist.Items.Clear();
            }
        }

        private void UpdatePlaylistUI() {
            lstPlaylists.ItemsSource = null;
            lstPlaylists.ItemsSource = _playlists;
        }

        private void LoadPlaylists() {
            try {
                if (!File.Exists(_playlistPath)) return;
                _playlists.Clear();
                foreach (var line in File.ReadAllLines(_playlistPath)) {
                    var parts = line.Split('|');
                    if (parts.Length >= 2 && Directory.Exists(parts[1])) {
                        _playlists.Add(new PlaylistFolder { Name = parts[0], Path = parts[1] });
                    }
                }
                UpdatePlaylistUI();
            } catch {}
        }

        private void SavePlaylists() {
            try {
                var lines = _playlists.Select(p => $"{p.Name}|{p.Path}");
                File.WriteAllLines(_playlistPath, lines);
            } catch {}
        }

        public void LoadPlaylist(string path, bool notify = true) {
            try {
                if (!Directory.Exists(path)) return;
                
                _playlist.Clear(); 
                lstPlaylist.Items.Clear();
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(s => s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                                s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                s.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)).ToList();
                
                foreach (var f in files) { 
                    _playlist.Add(f); 
                    lstPlaylist.Items.Add(Path.GetFileName(f)); 
                }

                if (notify) {
                    lblStatus.Text = (_currentLang == "zh-CN") ? $"成功导入 {files.Count} 首歌曲！" : $"Imported {files.Count} tracks!";
                }
            } catch (Exception ex) {
                MessageBox.Show("加载列表失败: " + ex.Message);
            }
        }

        private void lstPlaylist_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (lstPlaylist.SelectedIndex != -1) PlayTrack(lstPlaylist.SelectedIndex);
        }

        private void PlayTrack(int index) {
            if (index < 0 || index >= _playlist.Count) return;
            _currentTrackIndex = index;
            string filePath = _playlist[index];
            lstPlaylist.SelectedIndex = index;
            txtFilePath.Text = filePath;
            _audioLooper.LoadAudio(filePath);
            _audioLooper.Play();
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e) => _audioLooper.Play();
        private void btnPause_Click(object sender, RoutedEventArgs e) => _audioLooper.Pause();
        private void btnStop_Click(object sender, RoutedEventArgs e) { 
            if (!string.IsNullOrEmpty(txtFilePath.Text)) { 
                _audioLooper.Play(); 
                _audioLooper.SeekToSample(0); 
            } 
        }
        private void btnPrev_Click(object sender, RoutedEventArgs e) => PlayTrack(Math.Max(0, _currentTrackIndex - 1));
        private void btnNext_Click(object sender, RoutedEventArgs e) => PlayTrack(Math.Min(_playlist.Count - 1, _currentTrackIndex + 1));

        private void ApplyLoopSettings() {
            if (_isUpdatingUI) return;
            if (long.TryParse(txtLoopSample.Text, out long start)) _audioLooper.SetLoopStartSample(start);
            if (long.TryParse(txtLoopEndSample.Text, out long end)) _audioLooper.SetLoopEndSample(end);
            UpdateCurrentConfig();
            UpdateSecLabels();
        }

        private void btnApplyLoop_Click(object sender, RoutedEventArgs e) {
            ApplyLoopSettings();
            if (long.TryParse(txtLoopEndSample.Text, out long end) && long.TryParse(txtLoopSample.Text, out long start)) {
                long previewOffset = _currentSampleRate * 3;
                _audioLooper.SeekToSample(Math.Max(start, end - previewOffset));
                _audioLooper.Play(); // 即使是暂停状态也强制开始试听
            }
        }

        private void Adjust_Click(object sender, RoutedEventArgs e) {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            var parts = btn.Tag.ToString().Split(':');
            double deltaSec = double.Parse(parts[1]);
            long delta = (long)(_currentSampleRate * deltaSec);
            TextBox target = (parts[0] == "Start") ? txtLoopSample : txtLoopEndSample;
            if (long.TryParse(target.Text, out long current)) 
                target.Text = Math.Max(0, Math.Min(_totalSamples, current + delta)).ToString();
        }

        private void txtLoopSample_TextChanged(object sender, TextChangedEventArgs e) { 
            UpdateSecLabels(); 
            if (btnPlay != null) btnPlay.IsEnabled = !string.IsNullOrEmpty(txtFilePath.Text) && !string.IsNullOrEmpty(txtLoopSample.Text); 
        }
        private void txtLoopEndSample_TextChanged(object sender, TextChangedEventArgs e) => UpdateSecLabels();

        private void UpdateSecLabels() {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;
            if (txtLoopStartSec != null && long.TryParse(txtLoopSample.Text, out long s)) 
                txtLoopStartSec.Text = ((double)s / _currentSampleRate).ToString("F2");
            if (txtLoopEndSec != null && long.TryParse(txtLoopEndSample.Text, out long end)) 
                txtLoopEndSec.Text = ((double)end / _currentSampleRate).ToString("F2");
            _isUpdatingUI = false;
        }

        private void txtLoopStartSec_TextChanged(object sender, TextChangedEventArgs e) {
            if (_isUpdatingUI) return;
            if (double.TryParse(txtLoopStartSec.Text, out double sec)) {
                _isUpdatingUI = true;
                txtLoopSample.Text = ((long)Math.Max(0, Math.Min(_totalSamples, sec * _currentSampleRate))).ToString();
                _isUpdatingUI = false;
            }
        }

        private void txtLoopEndSec_TextChanged(object sender, TextChangedEventArgs e) {
            if (_isUpdatingUI) return;
            if (double.TryParse(txtLoopEndSec.Text, out double sec)) {
                _isUpdatingUI = true;
                txtLoopEndSample.Text = ((long)Math.Max(0, Math.Min(_totalSamples, sec * _currentSampleRate))).ToString();
                _isUpdatingUI = false;
            }
        }

        private void trkProgress_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            // 点击进度条强制跳转
            var slider = sender as Slider;
            if (slider == null || slider.ActualWidth <= 0) return;
            
            _isDraggingProgress = true;
            _lastSeekTime = DateTime.Now; // 开始跳转，抑制定时器

            // 计算比例
            double x = e.GetPosition(slider).X;
            double ratio = x / slider.ActualWidth;
            ratio = Math.Max(0, Math.Min(1.0, ratio));
            
            // 立即更新 UI 状态
            slider.Value = ratio * slider.Maximum;
            
            // 执行音频跳转
            _audioLooper.Seek(ratio);
            
            // 给音频引擎一点响应时间，稍后再解除抑制
            _isDraggingProgress = false;
        }

        private void trkProgress_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) {
            _isDraggingProgress = true;
        }
        
        private void trkProgress_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {
            _lastSeekTime = DateTime.Now;
            _audioLooper.Seek(trkProgress.Value / trkProgress.Maximum);
            _isDraggingProgress = false;
        }

        private void trkVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (_audioLooper != null) _audioLooper.Volume = (float)e.NewValue / 100f;
        }

        private void btnSwitchLang_Click(object sender, RoutedEventArgs e) {
            _currentLang = (_currentLang == "zh-CN") ? "en-US" : "zh-CN";
            SaveSettings(); 
            ApplyLanguage();
        }

        private void LoadConfig() {
            try {
                if (!File.Exists(_configPath)) return;
                var lines = File.ReadAllLines(_configPath);
                for (int i = 1; i < lines.Length; i++) {
                    var p = lines[i].Split('|');
                    if (p.Length >= 4 && long.TryParse(p[1], out long total)) {
                        _loopConfigs[$"{p[0]}_{p[1]}"] = new LoopConfigItem { 
                            LoopStart = long.TryParse(p[2], out long s) ? s : 0, 
                            LoopEnd = long.TryParse(p[3], out long e) ? e : total 
                        };
                    }
                }
            } catch {}
        }

        private void SaveConfig() {
            try {
                var lines = new List<string> { "FileName|TotalSamples|LoopStart|LoopEnd" };
                foreach (var kvp in _loopConfigs) {
                    int last_ = kvp.Key.LastIndexOf('_');
                    if (last_ > 0) lines.Add($"{kvp.Key.Substring(0, last_)}|{kvp.Key.Substring(last_ + 1)}|{kvp.Value.LoopStart}|{kvp.Value.LoopEnd}");
                }
                File.WriteAllLines(_configPath, lines);
            } catch {}
        }

        private void UpdateCurrentConfig() {
            if (string.IsNullOrEmpty(_currentConfigKey)) return;
            if (!_loopConfigs.ContainsKey(_currentConfigKey)) _loopConfigs[_currentConfigKey] = new LoopConfigItem();
            long.TryParse(txtLoopSample.Text, out _loopConfigs[_currentConfigKey].LoopStart);
            long.TryParse(txtLoopEndSample.Text, out _loopConfigs[_currentConfigKey].LoopEnd);
        }

        private void LoadSettings() {
            try {
                if (!File.Exists(_settingsPath)) { 
                    _currentLang = System.Globalization.CultureInfo.InstalledUICulture.Name.StartsWith("zh") ? "zh-CN" : "en-US"; 
                    return; 
                }
                foreach (var l in File.ReadAllLines(_settingsPath)) {
                    if (l.StartsWith("Language=")) _currentLang = l.Substring(9).Trim();
                    if (l.StartsWith("RecentFolders=")) {
                        var paths = l.Substring("RecentFolders=".Length).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        _recentFolders = paths.Where(p => Directory.Exists(p)).ToList();
                    }
                }
            } catch {}
        }

        private void SaveSettings() { 
            try { 
                var lines = new List<string> { 
                    $"Language={_currentLang}", 
                    $"RecentFolders={string.Join(";", _recentFolders)}" 
                };
                File.WriteAllLines(_settingsPath, lines); 
            } catch {} 
        }

        private void ApplyLanguage() {
            bool isZh = (_currentLang == "zh-CN");
            this.Title = isZh ? "无缝循环音乐播放器" : "Seamless Loop Music Player";
            if (lblMyPlaylists != null) lblMyPlaylists.Text = isZh ? "我的歌单" : "My Playlists";
            if (lblTrackList != null) lblTrackList.Text = isZh ? "歌曲列表" : "Track List";
            if (btnAddPlaylist != null) btnAddPlaylist.ToolTip = isZh ? "添加新文件夹到歌单" : "Add folder to playlists";
            if (btnPlay != null) btnPlay.Content = isZh ? "播放" : "Play";
            if (btnPause != null) btnPause.Content = isZh ? "暂停" : "Pause";
            if (btnStop != null) btnStop.Content = isZh ? "重新播放" : "Replay";
            if (btnPrev != null) btnPrev.Content = isZh ? "<< 上一首" : "<< Prev";
            if (btnNext != null) btnNext.Content = isZh ? "下一首 >>" : "Next >>";
            if (btnApplyLoop != null) btnApplyLoop.Content = isZh ? "确认应用并试听" : "Apply & Preview";
            if (lblFilePath != null) lblFilePath.Text = isZh ? "音频路径：" : "File Path:";
            if (lblLoopStart != null) lblLoopStart.Text = isZh ? "循环起始采样数：" : "Loop Start Sample:";
            if (lblLoopEnd != null) lblLoopEnd.Text = isZh ? "循环结束采样数：" : "Loop End Sample:";
            if (btnSwitchLang != null) btnSwitchLang.Content = isZh ? "English" : "中文";
            if (lblStatus != null) lblStatus.Text = isZh ? "就绪" : "Ready";
            UpdateAudioInfoText();
        }

        private void UpdateAudioInfoText() {
            if (lblAudioInfo == null) return;
            bool isZh = (_currentLang == "zh-CN");
            if (_totalSamples == 0) {
                lblAudioInfo.Text = isZh ? "音频信息：尚未加载" : "Audio Info: Not Loaded";
            } else {
                lblAudioInfo.Text = isZh ? 
                    $"音频信息：Total {_totalSamples} | 采样率 {_currentSampleRate} Hz" : 
                    $"Audio Info: Total {_totalSamples} Samples | Rate: {_currentSampleRate} Hz";
            }
        }

        protected override void OnClosed(EventArgs e) {
            SaveConfig();
            _audioLooper?.Dispose();
            base.OnClosed(e);
        }
    }
}
