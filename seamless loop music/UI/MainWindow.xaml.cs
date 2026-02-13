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
        private Point _dragStartPoint;

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
                lblStatus.Text = msg; // 汇报进度，让 cpu 大人知道我在努力工作
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
                txtFilePath.Text = track.FilePath; // 这里！要把新房子的门牌号挂上去
                txtLoopSample.Text = track.LoopStart.ToString();
                txtLoopEndSample.Text = track.LoopEnd.ToString();
                btnResetAB.IsEnabled = _playerService.IsABMode;
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
                await _playerService.AddFolderToPlaylist(newId, folderPath);
                // 扫描完刷新一下当前显示的歌曲列表
                LoadPlaylistFromDb(newId);
            }
            // 5. 更新列表
            LoadPlaylists();
        }

        private void lstPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder) {
                LoadPlaylistFromDb(folder.Id);
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

        private void miManageFolders_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder)
            {
                var mgr = new UI.FolderManagerWindow(_playerService, folder.Id, folder.Name);
                mgr.Owner = this;
                mgr.ShowDialog();
                
                // 窗口关闭后，刷新一下当前列表，以防内容变动
                LoadPlaylists();
                LoadPlaylistFromDb(folder.Id);
            }
        }

        private async void miRefreshPlaylist_Click(object sender, RoutedEventArgs e) {
            var selectedFolders = lstPlaylists.SelectedItems.Cast<PlaylistFolder>()
                                    .Where(f => f.IsFolderLinked).ToList();
            if (selectedFolders.Count == 0) return;

            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            int count = 0;

            foreach (var folder in selectedFolders)
            {
                count++;
                lblStatus.Text = isZh ? $"正在批量刷新 ({count}/{selectedFolders.Count}): {folder.Name}..." 
                                     : $"Batch Refreshing ({count}/{selectedFolders.Count}): {folder.Name}...";
                
                await _playerService.RefreshPlaylist(folder.Id);
            }
            
            // 全部完成后更新 UI
            LoadPlaylists();
            
            // 如果当前选中的文件夹中有正在显示的，重载它
            if (lstPlaylists.SelectedItem is PlaylistFolder current)
            {
                LoadPlaylistFromDb(current.Id);
            }
            
            lblStatus.Text = isZh ? $"已完成 {selectedFolders.Count} 个歌单的批量刷新。" : $"Batch refresh of {selectedFolders.Count} playlists completed.";
        }

        private void miDeletePlaylist_Click(object sender, RoutedEventArgs e) {
            var selectedFolders = lstPlaylists.SelectedItems.Cast<PlaylistFolder>().ToList();
            if (selectedFolders.Count == 0) return;

            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            string msg = isZh ? $"确定要从库中移除这 {selectedFolders.Count} 个歌单吗？(磁盘文件不会被删除)" 
                              : $"Are you sure to remove {selectedFolders.Count} playlists? (Source files won't be deleted)";
            
            if (MessageBox.Show(msg, Properties.Resources.TitleDelete, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
                foreach (var folder in selectedFolders)
                {
                    _playerService.DeletePlaylist(folder.Id);
                    _playlists.Remove(folder);
                }
                lstPlaylist.Items.Clear();
                txtFilePath.Text = isZh ? "未选择歌单" : "No playlist selected";
                lblStatus.Text = isZh ? $"已批量删除 {selectedFolders.Count} 个歌单。" : $"Deleted {selectedFolders.Count} playlists.";
            }
        }

        private void lstPlaylists_ContextMenuOpened(object sender, RoutedEventArgs e)
        {
            var cm = sender as ContextMenu;
            if (cm == null) return;
            
            var selectedItems = lstPlaylists.SelectedItems.Cast<PlaylistFolder>().ToList();
            bool hasSelection = selectedItems.Count > 0;
            bool singleSelection = selectedItems.Count == 1;

            bool anyFolderBased = selectedItems.Any(f => f.IsFolderLinked);
            bool anyVirtual = selectedItems.Any(f => !f.IsFolderLinked);

            // 设置可见性
            miRefreshPlaylist.Visibility = anyFolderBased ? Visibility.Visible : Visibility.Collapsed;
            miAddSong.Visibility = (singleSelection && anyVirtual) ? Visibility.Visible : Visibility.Collapsed;
            
            // 重命名只支持单选
            miRenamePlaylist.Visibility = singleSelection ? Visibility.Visible : Visibility.Collapsed;
            miManageFolders.Visibility = (singleSelection && anyFolderBased) ? Visibility.Visible : Visibility.Collapsed;
            miDeletePlaylist.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;

            // 如果没选中任何项，全部崩坏
            if (!hasSelection)
            {
                miRefreshPlaylist.Visibility = Visibility.Collapsed;
                miAddSong.Visibility = Visibility.Collapsed; 
                miRenamePlaylist.Visibility = Visibility.Collapsed;
                miManageFolders.Visibility = Visibility.Collapsed;
                miDeletePlaylist.Visibility = Visibility.Collapsed;
            }
        }

        private async void miAddSong_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylists.SelectedItem is PlaylistFolder folder && !folder.IsFolderLinked)
            {
                var dialog = new OpenFileDialog
                {
                    Multiselect = true,
                    Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.m4a;*.wma|All Files|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                    lblStatus.Text = isZh ? $"正在添加 {dialog.FileNames.Length} 首歌曲..." : $"Adding {dialog.FileNames.Length} tracks...";
                    
                    await _playerService.AddFilesToPlaylist(folder.Id, dialog.FileNames);
                    
                    // 刷新及显示
                    LoadPlaylists(); // 更新数量
                    LoadPlaylistFromDb(folder.Id); // 更新列表
                     
                    lblStatus.Text = isZh ? "添加完成" : "Done.";
                }
            }
        }


        private void LoadPlaylists() {
            try {
                // 保存当前选中的 ID，以便刷新后恢复
                var selectedId = (lstPlaylists.SelectedItem as PlaylistFolder)?.Id;
                
                _playlists.Clear();
                var list = _playerService.GetAllPlaylists();
                foreach (var p in list) {
                    _playlists.Add(p);
                }
                
                // 恢复选中
                if (selectedId != null) {
                    var item = _playlists.FirstOrDefault(p => p.Id == selectedId);
                    if (item != null) lstPlaylists.SelectedItem = item;
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



        private void lstTrackList_ContextMenuOpened(object sender, RoutedEventArgs e)
        {
            var cm = sender as ContextMenu;
            if (cm == null) return;
            
            bool hasSelection = lstPlaylist.SelectedItems.Count > 0;
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            
            // 找到占位符菜单项 "miAddToPlaylist"
            // 注意：因为我们是动态向 ContextMenu 填充的，直接找 x:Name 可能需要遍历 Items
            // 为了简单，我们每次清空再重建这一部分，或者利用 Tag 标记
            
            // 但其实 XAML 里已经定义了 miAddToPlaylist，我们只需操作它的 Items
            if (miAddToPlaylist != null)
            {
                miAddToPlaylist.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
                miAddToPlaylist.Items.Clear();
                miAddToPlaylist.Header = isZh ? "添加到歌单" : "Add to Playlist";
                
                if (hasSelection)
                {
                    // 获取所有【手动维护】的歌单
                    var manualPlaylists = _playerService.GetAllPlaylists().Where(p => !p.IsFolderLinked).ToList();
                    
                    if (manualPlaylists.Count == 0)
                    {
                        var emptyItem = new MenuItem { Header = isZh ? "(无可用歌单)" : "(No Playlists)", IsEnabled = false };
                        miAddToPlaylist.Items.Add(emptyItem);
                    }
                    else
                    {
                        foreach (var p in manualPlaylists)
                        {
                            var item = new MenuItem { Header = p.Name, Tag = p.Id };
                            item.Click += MiAddToSpecificPlaylist_Click;
                            miAddToPlaylist.Items.Add(item);
                        }
                    }
                }
            }
        }

        private void MiAddToSpecificPlaylist_Click(object sender, RoutedEventArgs e)
        {
             if (sender is MenuItem item && item.Tag is int playlistId)
             {
                 var tracks = lstPlaylist.SelectedItems.Cast<MusicTrack>().ToList();
                 if (tracks.Count == 0) return;
                 
                 _playerService.AddTracksToPlaylist(playlistId, tracks);
                 
                 bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                 string msg = isZh ? $"已将 {tracks.Count} 首歌曲添加到歌单。" : $"Added {tracks.Count} tracks to playlist.";
                 lblStatus.Text = msg;
                 
                 // 如果左侧正好显示的是这个目标歌单，更新一下它的数量显示
                 var pItem = _playlists.FirstOrDefault(p => p.Id == playlistId);
                 if (pItem != null) 
                 {
                     // 为了更新数量，最简单的是重载列表，但会闪烁
                     // 这里我们简单调用 LoadPlaylists 更新所有数量
                     LoadPlaylists();
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
                var tracksToRemove = lstPlaylist.SelectedItems.Cast<MusicTrack>().ToList();
                if (lstPlaylists.SelectedItem is PlaylistFolder folder)
                {
                    foreach (var t in tracksToRemove)
                    {
                        _playerService.RemoveTrackFromPlaylist(folder.Id, t.Id);
                        lstPlaylist.Items.Remove(t); 
                    }
                    _playerService.Playlist = lstPlaylist.Items.Cast<MusicTrack>().ToList();
                    LoadPlaylists(); // 更新数量
                }
                else
                {
                    // 只是从当前播放列表移除（非数据库歌单模式）
                    foreach (var t in tracksToRemove)
                    {
                        lstPlaylist.Items.Remove(t);
                    }
                }
            }
        }

        private async void miBatchPyLoop_Click(object sender, RoutedEventArgs e)
        {
            var tracks = lstPlaylist.SelectedItems.Cast<MusicTrack>().ToList();
            if (tracks.Count == 0) return;

            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            var confirm = MessageBox.Show(
                isZh ? $"确定要对选中的 {tracks.Count} 首歌曲进行深度循环分析吗？\n由于调用外部工具，这可能需要较长时间。" 
                     : $"Are you sure you want to perform deep loop analysis on the selected {tracks.Count} tracks?\nThis may take a long time.",
                isZh ? "批量极致匹配" : "Batch PyLoop Match",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // 禁用界面上相关的按钮防止冲突（可选，但安全）
            btnPyLoop.IsEnabled = false;

            await _playerService.BatchSmartMatchLoopExternalAsync(tracks, 
                (current, total, fileName) => {
                    Dispatcher.Invoke(() => {
                        lblStatus.Text = isZh ? $"[批量进度 {current}/{total}] 正在分析: {fileName}..." 
                                             : $"[Batch {current}/{total}] Analyzing: {fileName}...";
                    });
                },
                () => {
                    Dispatcher.Invoke(() => {
                        lblStatus.Text = isZh ? "批量极致匹配任务完成！" : "Batch PyLoop tasks completed!";
                        btnPyLoop.IsEnabled = true;
                        // 刷新一下列表显示，防止数据变了 UI 没动
                        lstPlaylist.Items.Refresh();
                        UpdateLoopUI(); // 如果当前播放的正是在批量列表里，更新 UI
                    });
                });
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

        private void btnPyLoop_Click(object sender, RoutedEventArgs e)
        {
            ApplyLoopSettings();
            btnPyLoop.IsEnabled = false;

            _playerService.SmartMatchLoopExternalAsync(() => {
                Dispatcher.Invoke(() => {
                    UpdateLoopUI();
                    btnPyLoop.IsEnabled = true;
                });
            });
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
            if (miRefreshPlaylist != null) miRefreshPlaylist.Header = isZh ? "刷新歌单" : "Refresh Playlist";
            if (miAddSong != null) miAddSong.Header = isZh ? "添加文件" : "Add Files";
            if (miRenameTrack != null) miRenameTrack.Header = isZh ? "修改别名" : "Rename Alias";
            if (miOpenFolder != null) miOpenFolder.Header = isZh ? "打开所在文件夹" : "Open Folder";
            if (miRemoveFromList != null) miRemoveFromList.Header = isZh ? "从歌单移除" : "Remove from List";
            if (miBatchPyLoop != null) miBatchPyLoop.Header = isZh ? "批量极致匹配 (PyLoop)" : "Batch Optimize (PyLoop)";
            
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
        // 显式设置双语文本，不再依赖资源文件可能缺失的键
        btnSmartMatch.Content = isZh ? "寻找起点" : "Match Start";
        btnSmartMatch.ToolTip = isZh ? "根据终点寻找循环起点 (逆向)" : "Find loop start based on loop end (Reverse)";
    }
    
    if (btnSmartMatchForward != null) {
        btnSmartMatchForward.Content = isZh ? "寻找终点" : "Match End";
        btnSmartMatchForward.ToolTip = isZh ? "根据起点寻找循环终点 (正向)" : "Find loop end based on loop start (Forward)";
    }
    
            if (btnPyLoop != null) {
                btnPyLoop.Content = isZh ? "极致匹配" : "PyLoop Match";
                btnPyLoop.ToolTip = isZh ? "使用 PyMusicLooper 算法寻找全曲最佳循环 (由于涉及深层分析，可能需要较长时间)" : "Find the best loop points in the entire song using PyMusicLooper (Deep analysis, might take a while)";
            }

            if (btnResetAB != null) {
                btnResetAB.Content = isZh ? "恢复AB接缝" : "Reset A/B";
                btnResetAB.ToolTip = isZh ? "针对 A/B 分体音乐，恢复到原始的物理接缝位置" : "For A/B split tracks, restore to the original physical boundary.";
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
        private void lstPlaylists_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void lstPlaylists_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    ListBox listBox = sender as ListBox;
                    PlaylistFolder folder = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as PlaylistFolder;
                    if (folder != null)
                    {
                        DragDrop.DoDragDrop(listBox, folder, DragDropEffects.Move);
                    }
                }
            }
        }

        private void lstPlaylists_Drop(object sender, DragEventArgs e)
        {
            PlaylistFolder droppedData = e.Data.GetData(typeof(PlaylistFolder)) as PlaylistFolder;
            PlaylistFolder target = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as PlaylistFolder;

            if (droppedData != null && target != null && droppedData != target)
            {
                int oldIndex = _playlists.IndexOf(droppedData);
                int newIndex = _playlists.IndexOf(target);

                _playlists.Move(oldIndex, newIndex);
                _playerService.UpdatePlaylistsSortOrder(_playlists.Select(p => p.Id).ToList());
            }
        }

        private void lstPlaylist_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void lstPlaylist_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    ListBox listBox = sender as ListBox;
                    MusicTrack track = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as MusicTrack;
                    if (track != null)
                    {
                        DragDrop.DoDragDrop(listBox, track, DragDropEffects.Move);
                    }
                }
            }
        }

        private void lstPlaylist_Drop(object sender, DragEventArgs e)
        {
            MusicTrack droppedData = e.Data.GetData(typeof(MusicTrack)) as MusicTrack;
            MusicTrack target = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as MusicTrack;

            if (droppedData != null && target != null && droppedData != target)
            {
                int oldIndex = lstPlaylist.Items.IndexOf(droppedData);
                int newIndex = lstPlaylist.Items.IndexOf(target);

                if (oldIndex == -1 || newIndex == -1) return;

                // UI 更新
                lstPlaylist.Items.RemoveAt(oldIndex);
                lstPlaylist.Items.Insert(newIndex, droppedData);
                _playlist.RemoveAt(oldIndex);
                _playlist.Insert(newIndex, droppedData.FilePath);

                // Service 同步
                var tracks = lstPlaylist.Items.Cast<MusicTrack>().ToList();
                _playerService.Playlist = tracks;

                // 数据库持久化
                if (lstPlaylists.SelectedItem is PlaylistFolder folder)
                {
                    _playerService.UpdateTracksSortOrder(folder.Id, tracks.Select(t => t.Id).ToList());
                }
            }
        }

        private void ListBox_DragOver(object sender, DragEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                if (scrollViewer != null)
                {
                    double tolerance = 30;
                    double verticalPos = e.GetPosition(listBox).Y;

                    if (verticalPos < tolerance)
                    {
                        scrollViewer.LineUp();
                    }
                    else if (verticalPos > listBox.ActualHeight - tolerance)
                    {
                        scrollViewer.LineDown();
                    }
                }
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null) parent = (T)parentObject; // 冗余检查，修正逻辑
            if (parent != null) return parent;
            return FindParent<T>(parentObject);
        }

        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T)
                    return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }
    }
}
