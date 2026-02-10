using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using seamless_loop_music.Data;
using seamless_loop_music.Models;

namespace seamless_loop_music
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AudioLooper _audioLooper;
        private readonly DatabaseHelper _dbHelper;
        private List<string> _playlist = new List<string>();
        private int _currentTrackIndex = -1;

        private class PlaylistFolder { public string Name { get; set; } public string Path { get; set; } }

        private List<string> _recentFolders = new List<string>(); // Keep for backward compatibility or future use
        private List<PlaylistFolder> _playlists = new List<PlaylistFolder>();

        private string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loop_config.csv");
        private string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.conf");
        private string _playlistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlists.conf");
        private string _currentLang = "zh-CN";
        private string _lastLoadedFilePath = "";
        private string _lastPlaylistPath = ""; // 新增：记录最后选中的歌单

        private string _currentConfigKey = null;
        private int _currentSampleRate = 44100;
        private long _totalSamples = 0;
        private bool _isUpdatingUI = false;
        private bool _isDraggingProgress = false;
        private DateTime _lastSeekTime = DateTime.MinValue; // 新增：记录最后一次跳转的时间
        private DispatcherTimer _tmrUpdate;

        public MainWindow()
        {
            // 1. Load settings FIRST to set the correct culture before UI initializes
            LoadSettings(); 
            
            InitializeComponent();
            _audioLooper = new AudioLooper();
            _dbHelper = new DatabaseHelper();

            
            // 初始化定时器
            _tmrUpdate = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tmrUpdate.Tick += (s, e) => {
                // 如果用户正在交互（拖动中），或者刚刚完成跳转（0.2秒内），严禁定时器控制进度条
                if (_isDraggingProgress || (DateTime.Now - _lastSeekTime).TotalSeconds < 0.2) return;
                
                var current = _audioLooper.CurrentTime;
                var total = _audioLooper.TotalTime;
                
                // 仅更新文字
                lblTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
                
                if (total.TotalMilliseconds > 0) {
                    _isUpdatingUI = true; // 开启保护位，防止反馈循环
                    trkProgress.Value = (current.TotalMilliseconds / total.TotalMilliseconds) * trkProgress.Maximum;
                    _isUpdatingUI = false;
                }
            };
            
            LoadConfig();
            LoadPlaylists();
            
            try {
                ApplyLanguage(); 
            } catch (Exception ex) {
                MessageBox.Show("Language Load Error: " + ex.Message + "\n" + ex.StackTrace);
            }

            UpdateAudioInfoText();
            UpdateButtons(PlaybackState.Stopped);

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
                
                // --- SQLite Logic (Pure Fingerprint: Filename + Samples) ---
                var track = _dbHelper.GetTrack(txtFilePath.Text, total);

                if (track != null) {
                    if (track.LoopEnd <= 0) track.LoopEnd = total;
                    _audioLooper.SetLoopStartSample(track.LoopStart);
                    _audioLooper.SetLoopEndSample(track.LoopEnd);
                    txtLoopSample.Text = track.LoopStart.ToString();
                    txtLoopEndSample.Text = track.LoopEnd.ToString();
                    
                    // 如果有别名，更新状态栏或提示 (未来可以显示在标题)
                    if (!string.IsNullOrEmpty(track.DisplayName)) {
                        lblStatus.Text = $"{Properties.Resources.StatusPlaying}: {track.DisplayName}";
                    }
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

            // 新增：自动加载上次使用的歌单
            if (!string.IsNullOrEmpty(_lastPlaylistPath) && Directory.Exists(_lastPlaylistPath)) {
                // 尝试在歌单列表中找到对应的项并选中
                var folder = _playlists.FirstOrDefault(p => p.Path == _lastPlaylistPath);
                if (folder != null) {
                    lstPlaylists.SelectedItem = folder;
                    LoadPlaylist(folder.Path, false); // 加载但不发弹窗
                }
            }

            // 自动加载上次播放的文件（如果存在）
            if (!string.IsNullOrEmpty(_lastLoadedFilePath) && File.Exists(_lastLoadedFilePath)) {
                try {
                    txtFilePath.Text = _lastLoadedFilePath;
                    _audioLooper.LoadAudio(_lastLoadedFilePath);
                    
                    // 自动在列表中定位并高亮选中
                    int idx = _playlist.IndexOf(_lastLoadedFilePath);
                    if (idx != -1) {
                        _currentTrackIndex = idx;
                        lstPlaylist.SelectedIndex = idx;
                        lstPlaylist.ScrollIntoView(lstPlaylist.SelectedItem); 
                    }
                } catch { /* 忽略自动加载失败 */ }
            }
        }

        private void UpdateButtons(PlaybackState state) {
            bool hasFile = !string.IsNullOrEmpty(txtFilePath.Text);
            
            btnPlay.IsEnabled = hasFile;
            // Update Toggle Button Text
            if (state == PlaybackState.Playing) {
                // Use Resources for dynamic state
                btnPlay.Content = Properties.Resources.StatusPaused; // Using "Paused" text for the button that pauses? No, usually "Pause"
                // Wait, resx has key "Play" and "StatusPaused". 
                // Button Text Logic: If Playing, Button says "Pause". If Paused, Button says "Play".
                // I don't have "Pause" verb in Resources? I have "StatusPaused". 
                // Let's check resx: Play=Play/播放, StatusPaused=Paused/已暂停.
                // I miss the verb "Pause". I will use "StatusPaused" as a fallback or hardcoded for now if needed.
                // Actually my checks earlier showed I missed "Pause" verb.
                // Reverting to isZh check for safety on missing keys, but using Resources.Culture
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                
                btnPlay.Content = isZh ? "暂停" : "Pause"; 
                
                btnPlay.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 226, 175)); // Yellowish
                _tmrUpdate.Start(); 
                lblStatus.Text = Properties.Resources.StatusPlaying;
            } else {
                btnPlay.Content = Properties.Resources.Play;
                btnPlay.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161)); // Green
                if (state == PlaybackState.Paused) {
                    _tmrUpdate.Stop(); 
                    lblStatus.Text = Properties.Resources.StatusPaused;
                } else { // Stopped
                    _tmrUpdate.Stop(); 
                    lblStatus.Text = Properties.Resources.StatusReady; 
                    trkProgress.Value = 0; 
                    lblTime.Text = "00:00 / 00:00";
                }
            }
            btnStop.IsEnabled = btnPrev.IsEnabled = btnNext.IsEnabled = hasFile;
        }

        private void btnAddPlaylist_Click(object sender, RoutedEventArgs e) {
            var picker = new FolderPicker();
            // 使用新版选择器，传入当前窗口作为父窗口
            if (picker.ShowDialog(this)) {
                string folderPath = picker.ResultPath;
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
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                string title = isZh ? "重命名" : "Rename";
                string content = isZh ? "输入新的歌单名称：" : "Enter new playlist name:";
                
                string newName = Microsoft.VisualBasic.Interaction.InputBox(content, title, folder.Name);
                if (!string.IsNullOrEmpty(newName)) {
                    folder.Name = newName;
                    UpdatePlaylistUI();
                    SavePlaylists();
                }
            }
        }

        private void miDeletePlaylist_Click(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                if (MessageBox.Show(Properties.Resources.MsgDeleteConfirm, Properties.Resources.TitleDelete, MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                    _playlists.Remove(folder);
                    UpdatePlaylistUI();
                    SavePlaylists();
                    lstPlaylist.Items.Clear();
                }
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
                    bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                    lblStatus.Text = isZh ? $"成功导入 {files.Count} 首歌曲！" : $"Imported {files.Count} tracks!";
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

        private void btnPlay_Click(object sender, RoutedEventArgs e) {
            if (_audioLooper.PlaybackState == PlaybackState.Playing) {
                _audioLooper.Pause();
            } else {
                _audioLooper.Play();
            }
        }
        private void btnReplay_Click(object sender, RoutedEventArgs e) { 
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
            UpdateSecLabels();
        }

        private void btnApplyLoop_Click(object sender, RoutedEventArgs e) {
            ApplyLoopSettings();
            SaveConfig(); // 立即保存配置到 CSV
            if (long.TryParse(txtLoopEndSample.Text, out long end) && long.TryParse(txtLoopSample.Text, out long start)) {
                long previewOffset = _currentSampleRate * 3;
                _audioLooper.SeekToSample(Math.Max(start, end - previewOffset));
                _audioLooper.Play(); // 即使是暂停状态也强制开始试听
            }
        }

        private void btnSmartMatch_Click(object sender, RoutedEventArgs e) {
            if (long.TryParse(txtLoopSample.Text, out long start) && long.TryParse(txtLoopEndSample.Text, out long end)) {
                long newStart, newEnd;
                _audioLooper.FindBestLoopPoints(start, end, out newStart, out newEnd);
                
                // 更新UI
                txtLoopSample.Text = newStart.ToString();
                txtLoopEndSample.Text = newEnd.ToString();
                // 提示
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                lblStatus.Text = isZh ? "智能匹配完成" : "Smart Match Done";
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
            if (_isUpdatingUI) return;
            if (long.TryParse(txtLoopSample.Text, out long val)) {
                if (val > _totalSamples && _totalSamples > 0) {
                    val = _totalSamples;
                    _isUpdatingUI = true;
                    txtLoopSample.Text = val.ToString();
                    _isUpdatingUI = false;
                }
            }
            UpdateSecLabels(); 
            if (btnPlay != null) btnPlay.IsEnabled = !string.IsNullOrEmpty(txtFilePath.Text) && !string.IsNullOrEmpty(txtLoopSample.Text); 
        }
        private void txtLoopEndSample_TextChanged(object sender, TextChangedEventArgs e) {
            if (_isUpdatingUI) return;
            if (long.TryParse(txtLoopEndSample.Text, out long val)) {
                if (val > _totalSamples && _totalSamples > 0) {
                    val = _totalSamples;
                    _isUpdatingUI = true;
                    txtLoopEndSample.Text = val.ToString();
                    _isUpdatingUI = false;
                }
            }
            UpdateSecLabels();
        }

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

        private void trkProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            // 核心跳转逻辑：只有当不是程序更新（_isUpdatingUI=false）且不是正在拖动（_isDraggingProgress=false）时
            // 才认为这是一次“点击跳转”
            if (!_isUpdatingUI && !_isDraggingProgress) {
                _lastSeekTime = DateTime.Now;
                _audioLooper.Seek(e.NewValue / trkProgress.Maximum);
            }
        }

        private void trkProgress_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) {
            _isDraggingProgress = true;
        }
        
        private void trkProgress_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {
            // 拖动结束时的跳转
            _lastSeekTime = DateTime.Now;
            _audioLooper.Seek(trkProgress.Value / trkProgress.Maximum);
            _isDraggingProgress = false; // 放在跳转之后，确保跳转时定时器还没接管
        }

        private void trkVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (_audioLooper != null) _audioLooper.Volume = (float)e.NewValue / 100f;
        }

        private void btnSwitchLang_Click(object sender, RoutedEventArgs e) {
            // Toggle config
            _currentLang = (_currentLang == "zh-CN") ? "en-US" : "zh-CN";
            
            // Set Culture
            var culture = new System.Globalization.CultureInfo(_currentLang);
            Properties.Resources.Culture = culture;
            
            SaveSettings();
            
            // Notify & Auto Restart
            bool isZh = _currentLang == "zh-CN";
            string title = isZh ? "语言切换" : "Language Switch";
            string msg = isZh ? "语言已切换，需要重启软件生效。现在重启吗？" : "Language switched. Restart now to apply?";
            
            if (MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                // Restart logic
                System.Diagnostics.Process.Start(System.Windows.Application.ResourceAssembly.Location);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void LoadConfig() {
            try {
                // 1. 检查是否需要从 CSV 迁移
                if (File.Exists(_configPath)) {
                    MigrateCsvToSqlite();
                }
                // 2. 现在统一走 SQLite，无需预加载全量 Dictionary
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
                        string fileName = Path.GetFileName(absPath);
                        
                        tracksToImport.Add(new MusicTrack {
                            FileName = fileName,
                            FilePath = absPath,
                            LoopStart = long.TryParse(p[2], out long s) ? s : 0,
                            LoopEnd = long.TryParse(p[3], out long e) ? e : total,
                            TotalSamples = total,
                            DisplayName = Path.GetFileNameWithoutExtension(absPath)
                        });
                    }
                }
                
                if (tracksToImport.Count > 0) {
                    _dbHelper.BulkInsert(tracksToImport);
                    // 迁移成功后备份旧文件
                    string bakPath = _configPath + ".bak";
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Move(_configPath, bakPath);
                    
                    bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                    MessageBox.Show(isZh ? $"成功从旧配置文件导入 {tracksToImport.Count} 条数据！" : $"Imported {tracksToImport.Count} entries from CSV!");
                }
            } catch {}
        }

        private void SaveConfig() {
            if (string.IsNullOrEmpty(txtFilePath.Text)) return;
            
            try {
                long.TryParse(txtLoopSample.Text, out long start);
                long.TryParse(txtLoopEndSample.Text, out long end);

                var track = _dbHelper.GetTrack(txtFilePath.Text, _totalSamples) 
                           ?? new MusicTrack { FileName = Path.GetFileName(txtFilePath.Text) };
                
                track.FilePath = txtFilePath.Text; 
                track.LoopStart = start;
                track.LoopEnd = end;
                track.TotalSamples = _totalSamples;
                
                _dbHelper.SaveTrack(track);
            } catch {}
        }


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
                    }
                }
                
                // IMPORTANT: Apply Culture immediately so InitializeComponent uses it
                try {
                    Properties.Resources.Culture = new System.Globalization.CultureInfo(_currentLang);
                } catch {
                    // Fallback to en-US if something is wrong
                    Properties.Resources.Culture = new System.Globalization.CultureInfo("en-US");
                }

            } catch {}
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
                
                // 新增：保存当前选中的歌单路径
                if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                    lines.Add($"LastPlaylist={folder.Path}");
                } else if (!string.IsNullOrEmpty(_lastPlaylistPath)) {
                    lines.Add($"LastPlaylist={_lastPlaylistPath}"); // 保留原有的
                }

                File.WriteAllLines(_settingsPath, lines); 
            } catch {} 
        }

        // ApplyLanguage removed as it is replaced by XAML binding
        private void ApplyLanguage() {
            // No strict "zh-CN" check needed if we trust Resources.Culture is set correctly in LoadSettings
            // But let's keep isZh for some logic if needed
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            this.Title = Properties.Resources.AppTitle; 
            if (lblMyPlaylists != null) lblMyPlaylists.Text = Properties.Resources.MyPlaylist;
            if (lblTrackList != null) lblTrackList.Text = Properties.Resources.TrackList;
            if (btnAddPlaylist != null) btnAddPlaylist.ToolTip = isZh ? "添加新文件夹到歌单" : "Add folder to playlists"; // Keep manual tooltip for now or add to resx
            
            if (btnPlay != null) {
                bool isPlaying = (_audioLooper != null && _audioLooper.PlaybackState == PlaybackState.Playing);
                if (isPlaying) btnPlay.Content = isZh ? "暂停" : "Pause"; // Manual fallback or use separate Resource keys for Pause
                else btnPlay.Content = Properties.Resources.Play;
            }
            if (btnStop != null) btnStop.Content = Properties.Resources.Replay;
            // Prev/Next are universal or can be added to resx later
            if (btnPrev != null) btnPrev.Content = isZh ? "<< 上一首" : "<< Prev";
            if (btnNext != null) btnNext.Content = isZh ? "下一首 >>" : "Next >>";

            if (btnApplyLoop != null) btnApplyLoop.Content = Properties.Resources.ApplyAndTest;
            if (btnSmartMatch != null) {
                btnSmartMatch.Content = Properties.Resources.SmartMatch;
                btnSmartMatch.ToolTip = isZh ? "自动微调循环点以匹配波形" : "Auto-adjust loop points to match waveform";
            }
            if (lblFilePath != null) lblFilePath.Text = Properties.Resources.FilePathTitle;
            if (lblLoopStart != null) lblLoopStart.Text = Properties.Resources.LoopStartLabel;
            if (lblLoopEnd != null) lblLoopEnd.Text = Properties.Resources.LoopEndLabel;
            if (btnSwitchLang != null) btnSwitchLang.Content = Properties.Resources.SwitchLang;
            if (lblStatus != null) lblStatus.Text = Properties.Resources.StatusReady;
            
            UpdateAudioInfoText();
        }

        private void UpdateAudioInfoText() {
            if (lblAudioInfo == null) return;
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            // Note: Resources.AudioInfoInit is used for the Not Loaded state in via XAML initially,
            // but we need to update it dynamically here.
            
            if (_totalSamples == 0) {
                 lblAudioInfo.Text = Properties.Resources.AudioInfoInit;
            } else {
                lblAudioInfo.Text = isZh ? 
                    $"音频信息：Total {_totalSamples} | 采样率 {_currentSampleRate} Hz" : 
                    $"Audio Info: Total {_totalSamples} Samples | Rate: {_currentSampleRate} Hz";
            }
        }

        protected override void OnClosed(EventArgs e) {
            SaveConfig();
            SavePlaylists();
            SaveSettings(); // 确保保存 LastFile 和其他设置
            _audioLooper?.Dispose();
            base.OnClosed(e);
        }
    }
}
