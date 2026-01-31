using System;
using System.Windows.Forms;

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
                lblAudioInfo.Text = $"音频信息：总采样数 {totalSamples} | 采样率 {rate} Hz";
            };
            _audioLooper.OnPlayStateChanged += (isPlaying) =>
            {
                btnPlay.Enabled = !isPlaying;
                btnStop.Enabled = isPlaying;
            };
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
                    // 启用播放相关控件（需先输入采样数）
                    btnPlay.Enabled = !string.IsNullOrEmpty(txtLoopSample.Text);
                }
            }
        }

        // 「播放」按钮点击事件
        private void btnPlay_Click(object sender, EventArgs e)
        {
            if (long.TryParse(txtLoopSample.Text, out long loopSample))
            {
                _audioLooper.SetLoopStartSample(loopSample);
                _audioLooper.Play();
            }
            else
            {
                lblStatus.Text = "请输入有效的循环起始采样数（整数）！";
            }
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
