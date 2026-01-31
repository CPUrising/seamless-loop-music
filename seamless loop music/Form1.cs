using System;
using System.Windows.Forms;
using NAudio.Wave; // 引入 NAudio 以使用 PlaybackState

namespace seamless_loop_music // 同样，命名空间和项目名称一致！
{
    public partial class Form1 : Form
    {
        private readonly AudioLooper _audioLooper;

        public Form1()
        {
            InitializeComponent();
            // 初始化音频循环器
            _audioLooper = new AudioLooper();
            // 绑定事件
            _audioLooper.OnStatusChanged += (msg) => lblStatus.Text = msg;
            
            // 新增: 监听音频信息加载
            _audioLooper.OnAudioLoaded += (totalSamples, rate) =>
            {
                // 使用 Invoke 确保在 UI 线程更新控件
                Invoke(new Action(() => 
                {
                    lblAudioInfo.Text = $"音频信息：总采样数 {totalSamples} | 采样率 {rate} Hz";
                    // 自动填入总采样数作为默认循环结束点
                    txtLoopEndSample.Text = totalSamples.ToString();
                    // 默认起始点设为 0
                    txtLoopSample.Text = "0";
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
            switch (state)
            {
                case PlaybackState.Playing:
                    btnPlay.Enabled = false;
                    btnPause.Enabled = true;
                    btnStop.Enabled = true;
                    tmrUpdate.Start(); // 开始更新进度
                    break;
                case PlaybackState.Paused:
                    btnPlay.Enabled = true;
                    btnPause.Enabled = false;
                    btnStop.Enabled = true;
                    tmrUpdate.Stop(); // 暂停更新进度
                    break;
                case PlaybackState.Stopped:
                    btnPlay.Enabled = !string.IsNullOrEmpty(txtFilePath.Text) && !string.IsNullOrEmpty(txtLoopSample.Text);
                    btnPause.Enabled = false;
                    btnStop.Enabled = false;
                    tmrUpdate.Stop(); // 停止更新
                    trkProgress.Value = 0; // 重置进度条
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

        // 「选择音频文件」按钮点击事件
        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "音频文件|*.wav;*.ogg;*.mp3|所有文件|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtFilePath.Text = ofd.FileName;
                    _audioLooper.LoadAudio(ofd.FileName);
                    // 触发一次状态检查以更新按钮
                    UpdateButtons(PlaybackState.Stopped);
                }
            }
        }

        // 「播放」按钮点击事件
        private void btnPlay_Click(object sender, EventArgs e)
        {
            // 设置循环起始点
            if (long.TryParse(txtLoopSample.Text, out long loopStart))
            {
                _audioLooper.SetLoopStartSample(loopStart);
            }

            // 设置循环结束点
            if (long.TryParse(txtLoopEndSample.Text, out long loopEnd))
            {
                _audioLooper.SetLoopEndSample(loopEnd);
            }
            else
            {
                 // 如果为空或解析失败，默认0
                 _audioLooper.SetLoopEndSample(0);
            }

            // 播放
            _audioLooper.Play();
        }



        private void txtLoopEndSample_TextChanged(object sender, EventArgs e)
        {
             // 实时更新 (可选)
        }

        // 「暂停」按钮点击事件
        private void btnPause_Click(object sender, EventArgs e)
        {
            _audioLooper.Pause();
        }

        // 「停止」按钮点击事件
        private void btnStop_Click(object sender, EventArgs e)
        {
            _audioLooper.Stop();
        }

        // 音量滑块拖动事件
        private void trkVolume_Scroll(object sender, EventArgs e)
        {
            _audioLooper.Volume = trkVolume.Value / 100f;
            lblStatus.Text = $"音量已设置为：{trkVolume.Value}%";
        }

        // 采样数输入框文本变化事件（启用播放按钮）
        private void txtLoopSample_TextChanged(object sender, EventArgs e)
        {
            btnPlay.Enabled = !string.IsNullOrEmpty(txtFilePath.Text) && !string.IsNullOrEmpty(txtLoopSample.Text);
        }

        // 窗体关闭时释放资源
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _audioLooper.Dispose();
        }
    }
}
