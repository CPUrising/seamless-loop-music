using System;
using System.Windows.Forms;
using NAudio.Wave; // 引入 NAudio 以使用 PlaybackState
using System.Collections.Generic;
using System.IO;
using System.Linq; // for Linq
using System.Drawing; // 补全绘图命名空间

namespace seamless_loop_music // 同样，命名空间和项目名称一致！
{
    public partial class Form1 : Form
    {
        private readonly AudioLooper _audioLooper;
        private List<string> _playlist = new List<string>();
        private int _currentTrackIndex = -1;

        // 配置存储相关
        private class LoopConfigItem 
        {
            public long LoopStart;
            public long LoopEnd;
        }
        // Key: "FileName_TotalSamples"
        private Dictionary<string, LoopConfigItem> _loopConfigs = new Dictionary<string, LoopConfigItem>();
        private string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loop_config.csv");
        private string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.conf");
        private string _currentLang = "zh-CN"; // 默认中文, 可选: "en-US"

        // 当前播放文件的标识，用于更新配置
        private string _currentConfigKey = null;
        private int _currentSampleRate = 44100;
        private long _totalSamples = 0;
        private bool _isUpdatingUI = false; // 加锁，防止初始化时误触自动保存

        public Form1()
        {
            InitializeComponent();
            LoadSettings(); // 加载全局设置 (语言)
            LoadConfig();   // 加载循环点配置
            ApplyLanguage(); // 应用语言

            // 初始化音频循环器
            _audioLooper = new AudioLooper();
            // 绑定事件
            // _audioLooper.OnStatusChanged += (msg) => lblStatus.Text = msg; // 移除直接绑定，改由 UI 根据状态自主控制多语言文本
            
            // 新增: 监听音频信息加载
            _audioLooper.OnAudioLoaded += (totalSamples, rate) =>
            {
                // 使用 Invoke 确保在 UI 线程更新控件
                Invoke(new Action(() => 
                {
                    _currentSampleRate = rate;
                    _totalSamples = totalSamples;

                    bool isZh = (_currentLang == "zh-CN");
                    string infoPrefix = isZh ? "音频信息：" : "Info: ";
                    string rateSuffix = isZh ? " 采样率 " : " Rate ";
                    lblAudioInfo.Text = $"{infoPrefix}Total {totalSamples} |{rateSuffix}{rate} Hz";
                    
                    // 生成当前文件的唯一 Key
                    string fileName = Path.GetFileName(txtFilePath.Text);
                    _currentConfigKey = $"{fileName}_{totalSamples}";

                    _isUpdatingUI = true; // 开始设置 UI，暂时关掉自动保存

                    // 尝试从配置加载
                    if (_loopConfigs.ContainsKey(_currentConfigKey))
                    {
                        var cfg = _loopConfigs[_currentConfigKey];
                        
                        // 关键修正：如果读取到的结束点是0或者还没设置，默认设为最大
                        if (cfg.LoopEnd <= 0) cfg.LoopEnd = totalSamples;

                        _audioLooper.SetLoopStartSample(cfg.LoopStart);
                        _audioLooper.SetLoopEndSample(cfg.LoopEnd);

                        txtLoopSample.Text = cfg.LoopStart.ToString();
                        txtLoopEndSample.Text = cfg.LoopEnd.ToString();
                    }
                    else
                    {
                        _audioLooper.SetLoopStartSample(0);
                        _audioLooper.SetLoopEndSample(totalSamples);

                        txtLoopSample.Text = "0";
                        txtLoopEndSample.Text = totalSamples.ToString();
                    }

                    _isUpdatingUI = false; // UI 设置完毕，恢复自动保存
                    ApplyLoopSettings();   // 统一应用一次
                }));
            };

            // 升级: 根据播放状态更新按钮
            _audioLooper.OnPlayStateChanged += (state) =>
            {
                // 使用 Invoke 确保在 UI 线程执行
                if (InvokeRequired)
                {
                    Invoke(new Action(() => UpdateButtons(state)));
                }
                else
                {
                    UpdateButtons(state);
                }
            };
        }

        // 拖拽状态标记
        private bool _isDraggingProgress = false;

        private void UpdateButtons(PlaybackState state)
        {
            bool isZh = (_currentLang == "zh-CN");
            bool hasFile = !string.IsNullOrEmpty(txtFilePath.Text);

            // 播放与暂停互斥
            btnPlay.Enabled = hasFile && (state != PlaybackState.Playing);
            btnPause.Enabled = hasFile && (state == PlaybackState.Playing);
            
            // 重新播放只要有文件就永远可用
            btnStop.Enabled = hasFile;

            // 列表控制
            btnPrev.Enabled = hasFile;
            btnNext.Enabled = hasFile;

            // 状态文字更新
            switch (state)
            {
                case PlaybackState.Playing:
                    tmrUpdate.Start();
                    lblStatus.Text = isZh ? "播放中..." : "Playing...";
                    break;
                case PlaybackState.Paused:
                    tmrUpdate.Stop();
                    lblStatus.Text = isZh ? "已暂停" : "Paused";
                    break;
                case PlaybackState.Stopped:
                    tmrUpdate.Stop();
                    lblStatus.Text = isZh ? "就绪" : "Ready";
                    // 重置进度条和时间标签
                    trkProgress.Value = 0;
                    lblTime.Text = "00:00 / 00:00";
                    break;
            }
        }

        // 定时器: 更新进度条和时间标签
        private void tmrUpdate_Tick(object sender, EventArgs e)
        {
            if (_isDraggingProgress) return; // 拖拽时不更新，防止跳变

            var current = _audioLooper.CurrentTime;
            var total = _audioLooper.TotalTime;

            // 更新时间标签
            lblTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";

            // 更新进度条
            if (total.TotalMilliseconds > 0)
            {
                int value = (int)((current.TotalMilliseconds / total.TotalMilliseconds) * trkProgress.Maximum);
                trkProgress.Value = Math.Min(value, trkProgress.Maximum);
            }
        }

        // 鼠标按下进度条: 标记拖拽中
        private void trkProgress_MouseDown(object sender, MouseEventArgs e)
        {
            _isDraggingProgress = true;
            
            // 可选: 支持点击跳转 (WinForms 默认 TrackBar 点击只能跳一格，这里加上点击即跳转逻辑)
            double percent = (double)e.X / trkProgress.Width;
            trkProgress.Value = (int)(percent * trkProgress.Maximum);
        }

        // 鼠标松开进度条: 执行跳转
        private void trkProgress_MouseUp(object sender, MouseEventArgs e)
        {
            _isDraggingProgress = false;
            
            // 计算百分比并跳转
            double percent = (double)trkProgress.Value / trkProgress.Maximum;
            _audioLooper.Seek(percent);
        }

        // 「导入音乐文件夹」按钮点击事件
        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                bool isZh = (_currentLang == "zh-CN");
                fbd.Description = isZh ? "请选择包含音频文件的文件夹" : "Select a folder containing audio files";
                

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string path = fbd.SelectedPath;
                    LoadPlaylist(path);
                }
            }
        }

        // 加载文件夹到播放列表
        private void LoadPlaylist(string folderPath)
        {
            try
            {
                // 清空旧列表
                _playlist.Clear();
                lstPlaylist.Items.Clear();
                _currentTrackIndex = -1;

                // 递归查找所有音频
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(s => s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                                s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                s.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count == 0)
                {
                    bool isZh = (_currentLang == "zh-CN");
                    string msg = isZh ? "该文件夹内未找到支持的音频文件 (wav/mp3/ogg)！" : "No supported audio files (wav/mp3/ogg) found!";
                    string title = isZh ? "提示" : "Info";
                    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (var f in files)
                {
                    _playlist.Add(f);
                    lstPlaylist.Items.Add(Path.GetFileName(f));
                }

                bool isZh2 = (_currentLang == "zh-CN");
                string successMsg = isZh2 ? $"成功导入 {files.Count} 首歌曲！" : $"Imported {files.Count} tracks!";
                string successTitle = isZh2 ? "成功" : "Success";
                MessageBox.Show(successMsg, successTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                // 自动选中第一首但不自动播放? 或者不选中? 
                // 为了方便，还是重置一下UI状态吧
                UpdateButtons(PlaybackState.Stopped);
                lblStatus.Text = successMsg;
            }
            catch (Exception ex)
            {
                bool isZh = (_currentLang == "zh-CN");
                MessageBox.Show((isZh ? "导入失败: " : "Import Error: ") + ex.Message, isZh ? "错误" : "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

            // 「播放」按钮点击事件
        private void btnPlay_Click(object sender, EventArgs e)
        {
            // 播放
            _audioLooper.Play();
        }



        // 采样数输入框文本变化事件 - 实时应用设置
        private void txtLoopSample_TextChanged(object sender, EventArgs e)
        {
            UpdateSecLabels();
            btnPlay.Enabled = !string.IsNullOrEmpty(txtFilePath.Text) && !string.IsNullOrEmpty(txtLoopSample.Text);
        }

        private void txtLoopEndSample_TextChanged(object sender, EventArgs e)
        {
            UpdateSecLabels();
        }

        // 核心应用设置 logic (原 btnApplyLoop_Click)
        private void ApplyLoopSettings()
        {
            if (_isUpdatingUI) return; // UI正在被莱芙自己修改时，不要反向覆盖配置！

            // 1. 应用设置到 AudioLooper
             if (long.TryParse(txtLoopSample.Text, out long loopStart))
            {
                _audioLooper.SetLoopStartSample(loopStart);
            }
            if (long.TryParse(txtLoopEndSample.Text, out long loopEnd))
            {
                 _audioLooper.SetLoopEndSample(loopEnd);
            }

            // 2. 保存到内存字典
            UpdateCurrentConfig();

            // 3. 更新秒数显示
            UpdateSecLabels();
        }

        private void UpdateSecLabels()
        {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;

            if (long.TryParse(txtLoopSample.Text, out long start))
            {
                double sec = (double)start / _currentSampleRate;
                txtLoopStartSec.Text = sec.ToString("F2");
            }
            if (long.TryParse(txtLoopEndSample.Text, out long end))
            {
                double sec = (double)end / _currentSampleRate;
                txtLoopEndSec.Text = sec.ToString("F2");
            }

            _isUpdatingUI = false;
        }

        private void txtLoopStartSec_TextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUI) return;
            if (double.TryParse(txtLoopStartSec.Text, out double sec))
            {
                _isUpdatingUI = true;
                long samples = (long)(sec * _currentSampleRate);
                if (samples < 0) samples = 0;
                if (samples > _totalSamples) samples = _totalSamples;
                txtLoopSample.Text = samples.ToString();
                _isUpdatingUI = false;
            }
        }

        private void txtLoopEndSec_TextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUI) return;
            if (double.TryParse(txtLoopEndSec.Text, out double sec))
            {
                _isUpdatingUI = true;
                long samples = (long)(sec * _currentSampleRate);
                if (samples < 0) samples = 0;
                if (samples > _totalSamples) samples = _totalSamples;
                txtLoopEndSample.Text = samples.ToString();
                _isUpdatingUI = false;
            }
        }

        private void btnApplyLoop_Click(object sender, EventArgs e)
        {
             // 1. 确认生效并保存配置
             ApplyLoopSettings();

             // 2. 实现跳转：从结束点前3秒试听
             if (long.TryParse(txtLoopEndSample.Text, out long loopEnd) &&
                 long.TryParse(txtLoopSample.Text, out long loopStart))
             {
                 // 跳转位置 = 结束点 - 3秒采样数
                 long previewOffset = _currentSampleRate * 3;
                 long jumpSample = loopEnd - previewOffset;

                 // 如果此时距离起点不足3秒，或者计算出的位置在起点之前，就直接跳转到起点
                 if (jumpSample < loopStart)
                 {
                     jumpSample = loopStart;
                 }

                 _audioLooper.SeekToSample(jumpSample);
             }
        }

        // --- 微调逻辑 ---
        private void btnAdjust_Click(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                string tagStr = btn.Tag.ToString();
                string[] parts = tagStr.Split(':');
                if (parts.Length == 2)
                {
                    string targetType = parts[0]; // "Start" or "End"
                    double seconds = double.Parse(parts[1]); // e.g. 1.0, 0.5, or 0.0 for frames
                    
                    long delta;
                    if (seconds == 0) // Special marker 0 for 500 frames
                    {
                        // Use sign to differentiate +/- 500
                        delta = tagStr.Contains("-") ? -500 : 500;
                    }
                    else
                    {
                        delta = (long)(_currentSampleRate * seconds);
                    }

                    TextBox targetTxt = (targetType == "Start") ? txtLoopSample : txtLoopEndSample;
                    AdjustSample(targetTxt, delta);
                }
            }
        }

        private void AdjustSample(TextBox target, long delta)
        {
            if (long.TryParse(target.Text, out long currentVal))
            {
                long newVal = currentVal + delta;
                // Boundary checks
                if (newVal < 0) newVal = 0;
                if (newVal > _totalSamples) newVal = _totalSamples;

                target.Text = newVal.ToString();
            }
        }

        private void btnSwitchLang_Click(object sender, EventArgs e)
        {
            _currentLang = (_currentLang == "zh-CN") ? "en-US" : "zh-CN";
            SaveSettings();
            ApplyLanguage();
        }

        // 「暂停」按钮点击事件
        private void btnPause_Click(object sender, EventArgs e)
        {
            _audioLooper.Pause();
        }

        // 「暂停」按钮点击事件
        private void btnStop_Click(object sender, EventArgs e)
        {
            // 重新播放：无视当前状态，确保重新起航
            if (string.IsNullOrEmpty(txtFilePath.Text)) return;
            
            // 关键：先启动引擎（如果没在播的话），再精准空降 0 秒位
            _audioLooper.Play();
            _audioLooper.SeekToSample(0);
        }

        // 音量滑块拖动事件
        private void trkVolume_Scroll(object sender, EventArgs e)
        {
            _audioLooper.Volume = trkVolume.Value / 100f;
            bool isZh = (_currentLang == "zh-CN");
            lblStatus.Text = isZh ? $"音量已设置为：{trkVolume.Value}%" : $"Volume set to: {trkVolume.Value}%";
        }

        // 窗体关闭时释放资源
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            SaveConfig(); // 保存配置
            _audioLooper.Dispose();
        }

        // --- 配置管理逻辑 ---

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath)) return;

                var lines = File.ReadAllLines(_configPath);
                // Skip header
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split('|'); // 使用 | 分隔
                    if (parts.Length >= 4) 
                    {
                        // 先解析 TotalSamples (parts[1])，因为它是 Key 的一部分，也是 LoopEnd 的默认值
                        if (long.TryParse(parts[1], out long totalSamples))
                        {
                            string key = $"{parts[0]}_{parts[1]}"; // FileName_TotalSamples
                            
                            // 解析 LoopStart (parts[2])，失败则默认为 0
                            long start = 0;
                            if (!string.IsNullOrWhiteSpace(parts[2]))
                            {
                                long.TryParse(parts[2], out start);
                            }

                            // 解析 LoopEnd (parts[3])，失败则默认为 TotalSamples
                            long end = totalSamples;
                            if (!string.IsNullOrWhiteSpace(parts[3]))
                            {
                                if (!long.TryParse(parts[3], out end))
                                {
                                    end = totalSamples;
                                }
                            }
                            
                            // 存入配置
                            _loopConfigs[key] = new LoopConfigItem { LoopStart = start, LoopEnd = end };
                        }
                    }
                }
            }
            catch { /* 忽略读取错误 */ }
        }

        private void SaveConfig()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("FileName|TotalSamples|LoopStart|LoopEnd"); // 使用 | 分隔 Header
                foreach (var kvp in _loopConfigs)
                {
                    // Key format: Name_Samples
                    // 我们得拆一下 Key 或者存的时候就保留原始信息。
                    // 简单起见，既然 Key = Name_Samples，那就 split 一下复原
                    int lastUnderline = kvp.Key.LastIndexOf('_');
                    if (lastUnderline > 0)
                    {
                        string name = kvp.Key.Substring(0, lastUnderline);
                        string samples = kvp.Key.Substring(lastUnderline + 1);
                        lines.Add($"{name}|{samples}|{kvp.Value.LoopStart}|{kvp.Value.LoopEnd}"); // 使用 | 分隔 Data
                    }
                }
                File.WriteAllLines(_configPath, lines);
            }
            catch { /* 忽略写入错误 */ }
        }

        private void UpdateCurrentConfig()
        {
            if (string.IsNullOrEmpty(_currentConfigKey)) return;

            long start = 0, end = 0;
            long.TryParse(txtLoopSample.Text, out start);
            long.TryParse(txtLoopEndSample.Text, out end);

            // 更新或添加
            if (!_loopConfigs.ContainsKey(_currentConfigKey))
            {
                _loopConfigs[_currentConfigKey] = new LoopConfigItem();
            }
            _loopConfigs[_currentConfigKey].LoopStart = start;
            _loopConfigs[_currentConfigKey].LoopEnd = end;
        }

        // --- 多语言支持 ---

        private void LoadSettings()
        {
            try 
            {
                // 如果没有配置文件，尝试检测系统语言
                if (!File.Exists(_settingsPath))
                {
                    var culture = System.Globalization.CultureInfo.InstalledUICulture;
                    // 简单的判断：如果是中文则 zh-CN，否则默认 en-US
                    if (culture.Name.StartsWith("zh")) _currentLang = "zh-CN";
                    else _currentLang = "en-US";
                    return;
                }
                
                // 读取配置
                var lines = File.ReadAllLines(_settingsPath);
                foreach(var line in lines) 
                {
                    if (line.StartsWith("Language=")) 
                    {
                        _currentLang = line.Substring("Language=".Length).Trim();
                    }
                }
            } 
            catch {}
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(_settingsPath, new string[] { $"Language={_currentLang}" });
            }
            catch {}
        }

        private void ApplyLanguage()
        {
            bool isZh = (_currentLang == "zh-CN");

            // 窗体标题
            this.Text = isZh ? "无缝循环音乐播放器" : "Seamless Loop Music Player";

            // 按钮
            btnSelectFile.Text = isZh ? "导入音乐文件夹" : "Import Music Folder";
            btnPlay.Text = isZh ? "播放" : "Play";
            btnPause.Text = isZh ? "暂停" : "Pause";
            btnStop.Text = isZh ? "重新播放" : "Replay";
            btnPrev.Text = isZh ? "<< 上一首" : "<< Prev";
            btnNext.Text = isZh ? "下一首 >>" : "Next >>";
            btnApplyLoop.Text = isZh ? "确认应用\n并试听" : "Apply &\nPreview";
            
            // 标签
            lblFilePath.Text = isZh ? "音频路径：" : "File Path:";
            // lblAudioInfo 是动态的，这里只更新前缀或者保留原状(它会被 Loaded 事件刷新)
            lblLoopSample.Text = isZh ? "循环起始采样数：" : "Loop Start Sample:";
            lblLoopEndSample.Text = isZh ? "循环结束采样数：" : "Loop End Sample:";
            
            // 切换按钮本身显示的文字 (显示"对方"的语言)
            btnSwitchLang.Text = isZh ? "English" : "中文";

            // 微调按钮 (s 在中英文中通用，无需特殊翻译，但保留位置以便后续扩展)
            
            // 更新一下状态栏
            lblStatus.Text = isZh ? "就绪" : "Ready";
        }

        // ListBox 双击播放
        private void lstPlaylist_DoubleClick(object sender, EventArgs e)
        {
            if (lstPlaylist.SelectedIndex != -1)
            {
                PlayTrack(lstPlaylist.SelectedIndex);
            }
        }

        // 上一首
        private void btnPrev_Click(object sender, EventArgs e)
        {
            if (_playlist.Count == 0 || _currentTrackIndex == -1) return;
            int newIndex = _currentTrackIndex - 1;
            if (newIndex < 0) newIndex = 0; // 到顶了就停在第一首
            PlayTrack(newIndex);
        }

        // 下一首
        private void btnNext_Click(object sender, EventArgs e)
        {
            if (_playlist.Count == 0 || _currentTrackIndex == -1) return;
            int newIndex = _currentTrackIndex + 1;
            if (newIndex >= _playlist.Count) newIndex = _playlist.Count - 1; // 到底了就停在最后一首
            PlayTrack(newIndex);
        }

        // 核心切歌逻辑
        private void PlayTrack(int index)
        {
            if (index < 0 || index >= _playlist.Count) return;

            _currentTrackIndex = index;
            string filePath = _playlist[index];

            // 选中列表
            lstPlaylist.SelectedIndex = index;
            
            // 更新路径框
            txtFilePath.Text = filePath;

            // 加载
            _audioLooper.LoadAudio(filePath);

            // 自动播放
            _audioLooper.Play();
        }


    }
}
