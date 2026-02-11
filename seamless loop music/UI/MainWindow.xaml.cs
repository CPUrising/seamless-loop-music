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

namespace seamless_loop_music
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 重构版：不仅负责貌美如花（UI），还把脏活累活全扔给了 PlayerService
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly PlayerService _playerService;
        
        private List<string> _playlist = new List<string>();
        private int _currentTrackIndex = -1;


        private List<string> _recentFolders = new List<string>(); 
        private ObservableCollection<PlaylistFolder> _playlists = new ObservableCollection<PlaylistFolder>();

        private string _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        private string _configPath;
        private string _settingsPath;
        // private string _playlistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlists.conf"); // 永久退役！
        
        private string _currentLang = "zh-CN";
        private string _lastLoadedFilePath = "";
        private string _lastPlaylistPath = ""; 
        private double _globalVolume = 80; 

        private bool _isUpdatingUI = false;
        private bool _isDraggingProgress = false;
        private DateTime _lastSeekTime = DateTime.MinValue; 
        private DispatcherTimer _tmrUpdate;

        public MainWindow()
        {
            // 0. 初始化数据目录与路径
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
            
            _configPath = Path.Combine(_dataDir, "loop_config.csv");
            _settingsPath = Path.Combine(_dataDir, "settings.conf");

            // 1. Load settings FIRST
            LoadSettings(); 
            
            InitializeComponent();
            
            // 2. 聘请音乐总监
            _playerService = new PlayerService();

            // 3. 订阅总监的信号
            _playerService.OnTrackLoaded += OnTrackLoaded;
            _playerService.OnPlayStateChanged += state => Dispatcher.Invoke(() => UpdateButtons(state));
            _playerService.OnStatusMessage += msg => Dispatcher.Invoke(() => {
                // 简单的状态反馈，不需要每次都弹窗，状态栏显示即可
                // 如果是 Error 开头，可能稍微醒目一点？暂且都显示在状态栏
                // lblStatus.Text = msg; // 暂时不覆盖播放状态
            });
            _playerService.OnIndexChanged += (index) => Dispatcher.Invoke(() => {
                if (lstPlaylist.SelectedIndex != index) {
                    _isUpdatingUI = true;
                    lstPlaylist.SelectedIndex = index;
                    lstPlaylist.ScrollIntoView(lstPlaylist.SelectedItem);
                    _isUpdatingUI = false;
                }
            });
            
            // 初始化定时器
            _tmrUpdate = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tmrUpdate.Tick += (s, e) => {
                if (_isDraggingProgress || (DateTime.Now - _lastSeekTime).TotalSeconds < 0.2) return;
                
                var current = _playerService.CurrentTime;
                var total = _playerService.TotalTime;
                
                lblTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
                
                if (total.TotalMilliseconds > 0) {
                    _isUpdatingUI = true; 
                    trkProgress.Value = (current.TotalMilliseconds / total.TotalMilliseconds) * trkProgress.Maximum;
                    _isUpdatingUI = false;
                }
            };
            
            LoadConfig(); // 仅负责迁移旧数据
            LoadPlaylists();
            
            try {
                ApplyLanguage(); 
            } catch (Exception ex) {
                MessageBox.Show("Language Load Error: " + ex.Message);
            }

            UpdateAudioInfoText();
            UpdateButtons(PlaybackState.Stopped);
            
            lstPlaylists.ItemsSource = _playlists;

            // 加载设置
            trkVolume.Value = _globalVolume;
            _playerService.SetVolume((float)(_globalVolume / 100.0));

            UpdateModeUI(); // 初始化模式按钮文字

            // 自动选中第一个歌单
            if (lstPlaylists.Items.Count > 0) lstPlaylists.SelectedIndex = 0;

            // 自动加载上次使用的歌单
            if (!string.IsNullOrEmpty(_lastPlaylistPath)) {
                // 尝试按 ID 匹配，如果失败（比如旧配置文件存的是路径），再按路径/名称匹配
                var folder = _playlists.FirstOrDefault(p => p.Id.ToString() == _lastPlaylistPath) 
                          ?? _playlists.FirstOrDefault(p => p.Path == _lastPlaylistPath || p.Name == _lastPlaylistPath);
                
                if (folder != null) {
                    lstPlaylists.SelectedItem = folder;
                    // LoadPlaylistFromDb 将在 SelectionChanged 中被触发
                }
            }

            // 自动加载上次播放的文件
            if (!string.IsNullOrEmpty(_lastLoadedFilePath) && File.Exists(_lastLoadedFilePath)) {
                try {
                    txtFilePath.Text = _lastLoadedFilePath;
                    _playerService.LoadTrack(_lastLoadedFilePath);
                    
                    int idx = _playlist.IndexOf(_lastLoadedFilePath);
                    if (idx != -1) {
                        _currentTrackIndex = idx;
                        lstPlaylist.SelectedIndex = idx;
                        lstPlaylist.ScrollIntoView(lstPlaylist.SelectedItem); 
                    }
                } catch { }
            }
        }

        private void OnTrackLoaded(MusicTrack track)
        {
            Dispatcher.Invoke(() => {
                UpdateAudioInfoText();

                _isUpdatingUI = true;
                
                // 更新显示
                txtLoopSample.Text = track.LoopStart.ToString();
                txtLoopEndSample.Text = track.LoopEnd.ToString();
                lblStatus.Text = $"{Properties.Resources.StatusPlaying}: {track.Title}";
                
                // 整理：如果列表里也有这个 Track，更新列表项显示
                // 必须通过文件路径精确查找，防止快速切歌时索引错位
                foreach (var item in lstPlaylist.Items) {
                    if (item is MusicTrack listTrack && listTrack.FilePath == track.FilePath) {
                        listTrack.Id = track.Id; // 同步 ID 很重要
                        listTrack.DisplayName = track.DisplayName;
                        listTrack.LoopStart = track.LoopStart;
                        listTrack.LoopEnd = track.LoopEnd;
                        listTrack.TotalSamples = track.TotalSamples;
                        break; // 找到了就停（假设列表里没有重复路径）
                    }
                }
                lstPlaylist.Items.Refresh();

                _isUpdatingUI = false;
                UpdateSecLabels(); // 计算秒数显示
            });
        }

        private void UpdateButtons(PlaybackState state) {
            bool hasFile = !string.IsNullOrEmpty(txtFilePath.Text);
            btnPlay.IsEnabled = hasFile;

            if (state == PlaybackState.Playing) {
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                btnPlay.Content = isZh ? "暂停" : "Pause"; 
                btnPlay.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 226, 175)); // Yellowish
                _tmrUpdate.Start(); 

                string trackName = Path.GetFileNameWithoutExtension(txtFilePath.Text);
                if (_playerService.CurrentTrack != null) trackName = _playerService.CurrentTrack.Title;
                lblStatus.Text = $"{Properties.Resources.StatusPlaying}: {trackName}";
            } else {
                btnPlay.Content = Properties.Resources.Play;
                btnPlay.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161)); // Green
                if (state == PlaybackState.Paused) {
                    _tmrUpdate.Stop(); 
                    lblStatus.Text = Properties.Resources.StatusPaused;
                } else { 
                    _tmrUpdate.Stop(); 
                    lblStatus.Text = Properties.Resources.StatusReady; 
                    trkProgress.Value = 0; 
                    lblTime.Text = "00:00 / 00:00";
                }
            }
            btnStop.IsEnabled = btnPrev.IsEnabled = btnNext.IsEnabled = hasFile;
        }

        private async void btnAddPlaylist_Click(object sender, RoutedEventArgs e) {
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            // 1. 询问是“新建空歌单”还是“导入文件夹”
            var choice = MessageBox.Show(
                isZh ? "点击'是'新建一个空的逻辑歌单，点击'否'通过导入文件夹创建歌单。" : "Click 'Yes' to create an empty virtual playlist, 'No' to create from folder.",
                isZh ? "新建歌单" : "New Playlist",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel) return;

            string folderName = "";
            string folderPath = null;
            bool isFolder = (choice == MessageBoxResult.No);

            if (isFolder) {
                var picker = new FolderPicker();
                if (picker.ShowDialog(this)) {
                    folderPath = picker.ResultPath;
                    folderName = Path.GetFileName(folderPath);
                    if (string.IsNullOrEmpty(folderName)) folderName = folderPath;
                } else return;
            } else {
                folderName = Microsoft.VisualBasic.Interaction.InputBox(
                    isZh ? "请输入歌单名称：" : "Enter playlist name:",
                    isZh ? "新建空歌单" : "New Virtual Playlist",
                    isZh ? "新建歌单" : "New Playlist");
                if (string.IsNullOrEmpty(folderName)) return;
            }

            // 2. 数据库建档
            int newId = _playerService.CreatePlaylist(folderName, folderPath, isLinked: isFolder);
            
            // 3. 刷新 UI
            LoadPlaylists();
            
            // 选中新歌单
            var newItem = _playlists.FirstOrDefault(p => p.Id == newId);
            if (newItem != null) lstPlaylists.SelectedItem = newItem;

            // 4. 如果是文件夹，启动后台扫描同步
            if (isFolder && !string.IsNullOrEmpty(folderPath)) {
                lblStatus.Text = isZh ? "正在扫描文件夹..." : "Scanning folder...";
                await _playerService.ScanAndAddFolderToPlaylist(newId, folderPath);
                // 扫描完刷新一下当前显示的歌曲列表
                if (lstPlaylists.SelectedItem == newItem) {
                    LoadPlaylistFromDb(newId);
                }
            }
        }

        private void lstPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                LoadPlaylistFromDb(folder.Id);
            }
        }

        private async void miAddFolderToPlaylist_Click(object sender, RoutedEventArgs e) {
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                var picker = new FolderPicker();
                if (picker.ShowDialog(this)) {
                    string folderPath = picker.ResultPath;
                    lblStatus.Text = isZh ? $"正在向 '{folder.Name}' 追加曲目..." : $"Appending tracks to '{folder.Name}'...";
                    
                    await _playerService.ScanAndAddFolderToPlaylist(folder.Id, folderPath);
                    
                    // 重新加载当前显示的曲目列表
                    LoadPlaylistFromDb(folder.Id);
                }
            } else {
                // 如果没选中，就 fallback 到新建逻辑
                btnAddPlaylist_Click(sender, e);
            }
        }

        private void miRenamePlaylist_Click(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                string newName = Microsoft.VisualBasic.Interaction.InputBox(
                    isZh ? "输入新的歌单名称：" : "Enter new playlist name:", 
                    isZh ? "重命名" : "Rename", 
                    folder.Name);
                if (!string.IsNullOrEmpty(newName)) {
                    folder.Name = newName;
                    _playerService.RenamePlaylist(folder.Id, newName);
                    lstPlaylists.Items.Refresh();
                }
            }
        }

        private void miDeletePlaylist_Click(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                if (MessageBox.Show(Properties.Resources.MsgDeleteConfirm, Properties.Resources.TitleDelete, MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                    _playerService.DeletePlaylist(folder.Id);
                    _playlists.Remove(folder);
                    lstPlaylist.Items.Clear();
                }
            }
        }


        private void LoadPlaylists() {
            try {
                _playlists.Clear();
                var list = _playerService.GetAllPlaylists();
                foreach (var p in list) {
                    _playlists.Add(p);
                }
            } catch {}
        }

        private void SavePlaylists() {
            // 已交给数据库实时保存，这里保持空壳或废弃
        }

        public void LoadPlaylistFromDb(int playlistId) {
            try {
                var tracks = _playerService.LoadPlaylistFromDb(playlistId);
                
                _playlist.Clear();
                lstPlaylist.Items.Clear();
                
                int missingCount = 0;
                foreach (var track in tracks) {
                    bool exists = File.Exists(track.FilePath);
                    if (!exists) {
                        missingCount++;
                        // 如果文件丢了，在显示名上打个标记，但不影响数据库数据
                        track.DisplayName = (Properties.Resources.Culture?.Name.StartsWith("zh") ?? false ? "[文件丢失] " : "[Missing] ") + (track.DisplayName ?? track.FileName);
                    }
                    
                    _playlist.Add(track.FilePath);
                    lstPlaylist.Items.Add(track);
                }
                
                _playerService.Playlist = tracks;
                
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                if (missingCount > 0) {
                    lblStatus.Text = isZh ? $"加载了 {tracks.Count} 首歌曲，其中 {missingCount} 首文件路径无效。" : $"Loaded {tracks.Count} tracks ({missingCount} files missing).";
                } else {
                    lblStatus.Text = isZh ? $"成功加载 {tracks.Count} 首歌曲" : $"Successfully loaded {tracks.Count} tracks";
                }
            } catch (Exception ex) {
                MessageBox.Show("加载歌单失败: " + ex.Message);
            }
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
                    var fileName = Path.GetFileName(f);
                    
                    // 委托 Service 快速查别名/配置
                    var track = _playerService.GetStoredTrackInfo(f);
                    
                    if (track == null) {
                        // 如果是全新的歌曲，创建一个占位符
                        track = new MusicTrack { FilePath = f, FileName = fileName };
                        
                        // 启动一个后台任务去预扫描采样数并入库，这样右键操作才有 ID 支撑
                        System.Threading.Tasks.Task.Run(() => {
                            long samples = _playerService.GetTotalSamples(f);
                            if (samples > 0) {
                                track.TotalSamples = samples;
                                track.LoopEnd = samples;
                                _playerService.UpdateOfflineTrack(track);
                            }
                        });
                    }

                    lstPlaylist.Items.Add(track); 
                }

                // 同步歌单给 Service
                _playerService.Playlist = lstPlaylist.Items.Cast<MusicTrack>().ToList();

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

        private void miRenameTrack_Click(object sender, RoutedEventArgs e) {
            if (lstPlaylist.SelectedItem is MusicTrack track) {
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                string newName = Microsoft.VisualBasic.Interaction.InputBox(
                    isZh ? $"给 '{track.FileName}' 起个别名：" : $"Set alias for '{track.FileName}':",
                    isZh ? "修改别名" : "Edit Alias",
                    track.DisplayName ?? track.Title);
                
                if (!string.IsNullOrEmpty(newName)) {
                    track.DisplayName = newName;
                    
                    // 如果正在播放这首歌，通知 Service 更新并入库
                    if (_playerService.CurrentTrack != null && _playerService.CurrentTrack.FilePath == track.FilePath) {
                        _playerService.RenameCurrentTrack(newName);
                    } else {
                        // 如果没在播放，利用 Service 提供的离线工具补全并保存
                        if (track.TotalSamples <= 0) {
                            track.TotalSamples = _playerService.GetTotalSamples(track.FilePath);
                            track.LoopEnd = track.TotalSamples; // 默认全曲
                        }
                        
                        // 2. 只有拿到采样数，才能入库
                        if (track.TotalSamples > 0) {
                            // 先查户口（合并旧配置，防止覆盖）
                            var stored = _playerService.GetStoredTrackInfo(track.FilePath);
                            if (stored != null) {
                                track.Id = stored.Id;
                                track.LoopStart = stored.LoopStart;
                                track.LoopEnd = (stored.LoopEnd <= 0) ? track.TotalSamples : stored.LoopEnd;
                            }
                            _playerService.UpdateOfflineTrack(track);
                        }
                    }
                    
                    lstPlaylist.Items.Refresh();
                }
            }
        }

        private void miOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylist.SelectedItem is MusicTrack track && File.Exists(track.FilePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
        }

        private void miRemoveFromList_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylist.SelectedItem is MusicTrack track)
            {
                if (lstPlaylists.SelectedItem is PlaylistFolder folder)
                {
                    _playerService.RemoveTrackFromPlaylist(folder.Id, track.Id);
                }
                lstPlaylist.Items.Remove(track);
                _playerService.Playlist = lstPlaylist.Items.Cast<MusicTrack>().ToList();
            }
        }

        private void PlayTrack(int index) {
            if (index < 0 || index >= _playlist.Count) return;
            _currentTrackIndex = index;
            string filePath = _playlist[index];
            lstPlaylist.SelectedIndex = index;
            txtFilePath.Text = filePath;
            
            _playerService.LoadTrack(filePath);
            _playerService.Play();
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e) {
            if (_playerService.PlaybackState == PlaybackState.Playing) {
                _playerService.Pause();
            } else {
                _playerService.Play();
            }
        }
        
        private void btnReplay_Click(object sender, RoutedEventArgs e) { 
            if (!string.IsNullOrEmpty(txtFilePath.Text)) { 
                _playerService.Play(); 
                _playerService.SeekToSample(0); 
            } 
        }
        private void btnPrev_Click(object sender, RoutedEventArgs e) => _playerService.PreviousTrack();
        private void btnNext_Click(object sender, RoutedEventArgs e) => _playerService.NextTrack();

        private void btnPlayMode_Click(object sender, RoutedEventArgs e)
        {
            // 切换模式：SingleLoop -> ListLoop -> Shuffle -> SingleLoop
            var nextMode = (PlayMode)(((int)_playerService.CurrentMode + 1) % 3);
            _playerService.CurrentMode = nextMode;
            UpdateModeUI();
        }

        private void UpdateModeUI()
        {
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            string modeText = "";
            switch (_playerService.CurrentMode)
            {
                case PlayMode.SingleLoop: modeText = isZh ? "模式：单曲" : "Mode: Single"; break;
                case PlayMode.ListLoop: modeText = isZh ? "模式：列表" : "Mode: List"; break;
                case PlayMode.Shuffle: modeText = isZh ? "模式：随机" : "Mode: Shuffle"; break;
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
                long previewOffset = _playerService.SampleRate * 3;
                _playerService.SeekToSample(Math.Max(start, end - previewOffset));
                _playerService.Play(); 
            }
        }

        private void btnSmartMatch_Click(object sender, RoutedEventArgs e) {
            // 先应用界面上输入的数值，否则匹配会基于旧值
            ApplyLoopSettings();
            
            _playerService.SmartMatchLoop();
            
            // 更新UI
            txtLoopSample.Text = _playerService.LoopStartSample.ToString();
            txtLoopEndSample.Text = _playerService.LoopEndSample.ToString();
            
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            lblStatus.Text = isZh ? "智能匹配完成" : "Smart Match Done";
        }

        private void Adjust_Click(object sender, RoutedEventArgs e) {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            var parts = btn.Tag.ToString().Split(':');
            double deltaSec = double.Parse(parts[1]);
            long delta = (long)(_playerService.SampleRate * deltaSec); // 使用 Service 中的 SampleRate
            
            TextBox target = (parts[0] == "Start") ? txtLoopSample : txtLoopEndSample;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            
            if (long.TryParse(target.Text, out long current)) 
                target.Text = Math.Max(0, Math.Min(total, current + delta)).ToString();
        }

        private void txtLoopSample_TextChanged(object sender, TextChangedEventArgs e) { 
            if (_playerService == null || _isUpdatingUI) return;
            long total = _playerService.CurrentTrack?.TotalSamples ?? 0;
            if (long.TryParse(txtLoopSample.Text, out long val)) {
                if (val > total && total > 0) {
                    _isUpdatingUI = true;
                    txtLoopSample.Text = total.ToString();
                    _isUpdatingUI = false;
                }
            }
            UpdateSecLabels(); 
            if (btnPlay != null) btnPlay.IsEnabled = !string.IsNullOrEmpty(txtFilePath.Text) && !string.IsNullOrEmpty(txtLoopSample.Text); 
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
            }
            UpdateSecLabels();
        }

        private void UpdateSecLabels() {
            if (_playerService == null || _isUpdatingUI) return;
            _isUpdatingUI = true;
            int rate = _playerService.SampleRate;
            if (rate <= 0) rate = 44100;

            if (txtLoopStartSec != null && long.TryParse(txtLoopSample.Text, out long s)) 
                txtLoopStartSec.Text = ((double)s / rate).ToString("F2");
            if (txtLoopEndSec != null && long.TryParse(txtLoopEndSample.Text, out long end)) 
                txtLoopEndSec.Text = ((double)end / rate).ToString("F2");
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

        private void trkProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (!_isUpdatingUI && !_isDraggingProgress) {
                _lastSeekTime = DateTime.Now;
                _playerService.Seek(e.NewValue / trkProgress.Maximum);
            }
        }

        private void trkProgress_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) {
            _isDraggingProgress = true;
        }
        
        private void trkProgress_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {
            _lastSeekTime = DateTime.Now;
            _playerService.Seek(trkProgress.Value / trkProgress.Maximum);
            _isDraggingProgress = false; 
        }

        private void trkVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (_playerService != null) _playerService.SetVolume((float)e.NewValue / 100f);
        }

        private void btnSwitchLang_Click(object sender, RoutedEventArgs e) {
            // Toggle config
            _currentLang = (_currentLang == "zh-CN") ? "en-US" : "zh-CN";
            
            // Set Culture
            var culture = new System.Globalization.CultureInfo(_currentLang);
            Properties.Resources.Culture = culture;
            
            SaveSettings();
            
            bool isZh = _currentLang == "zh-CN";
            string title = isZh ? "语言切换" : "Language Switch";
            string msg = isZh ? "语言已切换，需要重启软件生效。现在重启吗？" : "Language switched. Restart now to apply?";
            
            if (MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                System.Diagnostics.Process.Start(System.Windows.Application.ResourceAssembly.Location);
                System.Windows.Application.Current.Shutdown();
            }
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
                    
                    bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
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
                    }
                }
                
                try {
                    Properties.Resources.Culture = new System.Globalization.CultureInfo(_currentLang);
                } catch {
                    Properties.Resources.Culture = new System.Globalization.CultureInfo("en-US");
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
                
                if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                    lines.Add($"LastPlaylist={folder.Id}");
                }
 else if (!string.IsNullOrEmpty(_lastPlaylistPath)) {
                    lines.Add($"LastPlaylist={_lastPlaylistPath}");
                }

                lines.Add($"Volume={trkVolume.Value}");

                File.WriteAllLines(_settingsPath, lines); 
            } catch (Exception ex) {
                 System.Diagnostics.Debug.WriteLine($"SaveSettings Error: {ex.Message}");
            } 
        }

        private void ApplyLanguage() {
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            this.Title = Properties.Resources.AppTitle; 
            if (lblMyPlaylists != null) lblMyPlaylists.Text = Properties.Resources.MyPlaylist;
            if (lblTrackList != null) lblTrackList.Text = Properties.Resources.TrackList;
            if (btnAddPlaylist != null) btnAddPlaylist.ToolTip = isZh ? "添加新文件夹到歌单" : "Add folder to playlists";
            if (miAddPlaylist != null) miAddPlaylist.Header = isZh ? "添加文件夹" : "Add Folder";
            if (miRenameTrack != null) miRenameTrack.Header = isZh ? "修改别名" : "Rename Alias";
            if (miOpenFolder != null) miOpenFolder.Header = isZh ? "打开所在文件夹" : "Open Folder";
            if (miRemoveFromList != null) miRemoveFromList.Header = isZh ? "从歌单移除" : "Remove from List";
            
            if (btnPlay != null) {
                bool isPlaying = (_playerService != null && _playerService.PlaybackState == PlaybackState.Playing);
                if (isPlaying) btnPlay.Content = isZh ? "暂停" : "Pause";
                else btnPlay.Content = Properties.Resources.Play;
            }
            if (btnStop != null) btnStop.Content = Properties.Resources.Replay;
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
            
            UpdateModeUI(); // 更新模式按钮语言

            if (lblStatus != null) lblStatus.Text = Properties.Resources.StatusReady;
            
            UpdateAudioInfoText();
        }

        private void UpdateAudioInfoText() {
            if (lblAudioInfo == null) return;
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            long total = _playerService?.CurrentTrack?.TotalSamples ?? 0;
            int rate = _playerService?.SampleRate ?? 44100;

            if (total == 0) {
                 lblAudioInfo.Text = Properties.Resources.AudioInfoInit;
            } else {
                lblAudioInfo.Text = isZh ? 
                    $"音频信息：Total {total} | 采样率 {rate} Hz" : 
                    $"Audio Info: Total {total} Samples | Rate: {rate} Hz";
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
