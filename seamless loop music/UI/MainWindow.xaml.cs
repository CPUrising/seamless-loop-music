using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using NAudio.Wave;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 重构版：不仅负责貌美如花（UI），还把脏活累活全扔给了 ViewModel
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IPlayerService _playerService;
        
        private string _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        private string _configPath;
        private string _settingsPath;
        
        private string _currentLang = "zh-CN";
        private string _lastLoadedFilePath = "";
        private string _lastPlaylistPath = ""; 
        private List<string> _recentFolders = new List<string>();

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

            _playerService = playerService;

            // 1. Load settings FIRST
            LoadSettings(); 
            
            InitializeComponent();

            LoadConfig(); // 仅负责迁移旧数据
            
            try {
                ApplyLanguage(); 
            } catch (Exception ex) {
                // Ignore
            }

            // 自动加载上次播放的文件
            if (!string.IsNullOrEmpty(_lastLoadedFilePath) && File.Exists(_lastLoadedFilePath)) {
                try {
                    _playerService.LoadTrack(_lastLoadedFilePath);
                } catch { }
            }
        }

        private void LoadConfig() {
            try {
                if (File.Exists(_configPath)) {
                    MigrateCsvToSqlite();
                }
            } catch (Exception) {
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
                        if (l.StartsWith("Volume=") && double.TryParse(l.Substring(7), out double vol)) _playerService.Volume = (float)vol / 100f;
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
                if (_playerService.CurrentTrack != null) {
                    lines.Add($"LastFile={_playerService.CurrentTrack.FilePath}");
                }
                
                if (!string.IsNullOrEmpty(_lastPlaylistPath)) {
                    lines.Add($"LastPlaylist={_lastPlaylistPath}");
                }

                lines.Add($"Volume={_playerService.Volume * 100}");
                lines.Add($"MatchWindow={_playerService.MatchWindowSize}");
                lines.Add($"SearchRadius={_playerService.MatchSearchRadius}");

                File.WriteAllLines(_settingsPath, lines); 
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"SaveSettings Error: {ex.Message}");
            } 
        }

        private void ApplyLanguage() {
            // MainWindow level localization (e.g. window title is already bound)
        }

        protected override void OnClosed(EventArgs e) {
            SaveSettings(); 
            _playerService?.Dispose();
            base.OnClosed(e);
        }
    }
}
