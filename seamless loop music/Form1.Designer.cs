using System.Drawing;
using System.Windows.Forms;

namespace seamless_loop_music
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnSelectFile = new Button();
            btnPlay = new Button();
            btnPause = new Button(); // 补上这一行！
            btnStop = new Button();
            txtFilePath = new TextBox();
            lblFilePath = new Label();
            lblLoopSample = new Label();
            txtLoopSample = new TextBox();
            trkVolume = new TrackBar();
            lblStatus = new Label();
            lblAudioInfo = new Label(); // 初始化新标签
            tmrUpdate = new System.Windows.Forms.Timer(); // 去掉 components 参数，防止空引用！
            trkProgress = new TrackBar(); // 初始化进度条
            lblTime = new Label(); // 初始化时间标签
            lblLoopEndSample = new Label(); // 初始化循环结束标签
            txtLoopEndSample = new TextBox(); // 初始化循环结束输入框
            lstPlaylist = new ListBox();
            btnPrev = new Button();
            btnNext = new Button();
            btnSwitchLang = new Button(); // 语言切换按钮
            txtLoopStartSec = new TextBox();
            txtLoopEndSec = new TextBox();
            btnApplyLoop = new Button();
            
            // 初始化微调按钮
            btnStartM1s = new Button(); btnStartM05s = new Button(); btnStartM500 = new Button();
            btnStartP500 = new Button(); btnStartP05s = new Button(); btnStartP1s = new Button();
            btnEndM1s = new Button(); btnEndM05s = new Button(); btnEndM500 = new Button();
            btnEndP500 = new Button(); btnEndP05s = new Button(); btnEndP1s = new Button();
            
            ((System.ComponentModel.ISupportInitialize)trkVolume).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trkProgress).BeginInit(); // 别忘了这个
            SuspendLayout();
            // 
            // btnSelectFile
            // 
            btnSelectFile.Location = new Point(50, 25);
            btnSelectFile.Name = "btnSelectFile";
            btnSelectFile.Size = new Size(160, 45); // 高度统一为 45
            btnSelectFile.TabIndex = 4;
            btnSelectFile.Text = "导入音乐文件夹";
            btnSelectFile.UseVisualStyleBackColor = true;
            btnSelectFile.Click += btnSelectFile_Click;
            // 
            // btnPlay
            // 
            btnPlay.Enabled = false;
            btnPlay.Location = new Point(220, 25); // Y=25 对齐
            btnPlay.Name = "btnPlay";
            btnPlay.Size = new Size(100, 45); // 高度统一为 45
            btnPlay.TabIndex = 5;
            btnPlay.Text = "播放";
            btnPlay.UseVisualStyleBackColor = true;
            btnPlay.Click += btnPlay_Click;
            // 
            // btnPause
            // 
            btnPause.Enabled = false;
            btnPause.Location = new Point(330, 25); // Y=25 对齐
            btnPause.Name = "btnPause";
            btnPause.Size = new Size(100, 45); // 高度统一为 45
            btnPause.TabIndex = 6;
            btnPause.Text = "暂停";
            btnPause.UseVisualStyleBackColor = true;
            btnPause.Click += btnPause_Click;
            // 
            // btnStop
            // 
            btnStop.Enabled = false;
            btnStop.Location = new Point(440, 25); // Y=25 对齐
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(120, 45); // 高度统一为 45，宽度略宽
            btnStop.TabIndex = 7;
            btnStop.Text = "重新播放";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // txtFilePath
            // 
            txtFilePath.Location = new Point(230, 107); // X: 202 -> 230
            txtFilePath.Name = "txtFilePath";
            txtFilePath.ReadOnly = true;
            txtFilePath.ScrollBars = ScrollBars.Horizontal;
            txtFilePath.Size = new Size(360, 30);
            txtFilePath.TabIndex = 3;
            // 
            // lblFilePath
            // 
            lblFilePath.AutoSize = true;
            lblFilePath.Location = new Point(102, 103);
            lblFilePath.Name = "lblFilePath";
            lblFilePath.Size = new Size(100, 24);
            lblFilePath.TabIndex = 4;
            lblFilePath.Text = "\t音频路径：";
            // 
            // lblLoopSample
            // 
            lblLoopSample.AutoSize = true;
            lblLoopSample.Location = new Point(102, 193); // Y=193
            lblLoopSample.Name = "lblLoopSample";
            lblLoopSample.Size = new Size(154, 24);
            lblLoopSample.TabIndex = 6;
            lblLoopSample.Text = "循环起始采样数：";
            // 
            // txtLoopSample
            // 
            txtLoopSample.Location = new Point(320, 195); // 顶部对齐
            txtLoopSample.Name = "txtLoopSample";
            txtLoopSample.ScrollBars = ScrollBars.Horizontal;
            txtLoopSample.Size = new Size(170, 30);
            txtLoopSample.TabIndex = 5;
            txtLoopSample.TextChanged += txtLoopSample_TextChanged;
            // 
            // trkVolume
            // 
            trkVolume.Location = new Point(141, 465); // Y: 415 -> 465
            trkVolume.Maximum = 100;
            trkVolume.Name = "trkVolume";
            trkVolume.Size = new Size(236, 69);
            trkVolume.TabIndex = 7;
            trkVolume.Value = 100;
            trkVolume.Scroll += trkVolume_Scroll;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(233, 530); // Y: 480 -> 530
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(46, 24);
            lblStatus.TabIndex = 8;
            lblStatus.Text = "就绪";
            // 
            // lblAudioInfo
            // 
            lblAudioInfo.AutoSize = true;
            lblAudioInfo.ForeColor = SystemColors.GrayText;
            lblAudioInfo.Location = new Point(130, 145); // X: 102 -> 130
            lblAudioInfo.Name = "lblAudioInfo";
            lblAudioInfo.Size = new Size(0, 24);
            lblAudioInfo.TabIndex = 9;
            lblAudioInfo.Text = "---";
            lblAudioInfo.ForeColor = Color.Gray;
            // 
            // tmrUpdate
            // 
            tmrUpdate.Interval = 500;
            tmrUpdate.Tick += tmrUpdate_Tick;
            // 
            // trkProgress
            // 
            trkProgress.Location = new Point(141, 395); // Y: 345 -> 395
            trkProgress.Name = "trkProgress";
            trkProgress.Size = new Size(350, 69); // 比较宽
            trkProgress.TabIndex = 11;
            trkProgress.TickStyle = TickStyle.None; // 不需要刻度
            trkProgress.Maximum = 1000; // 精度高一点，方便平滑移动
            trkProgress.MouseDown += trkProgress_MouseDown;
            trkProgress.MouseUp += trkProgress_MouseUp;
            // 
            // lblTime
            // 
            lblTime.AutoSize = true;
            lblTime.Location = new Point(497, 400); // Y: 350 -> 400
            lblTime.Name = "lblTime";
            lblTime.Size = new Size(59, 24);
            lblTime.TabIndex = 12;
            lblTime.Text = "00:00";
            // 
            // lblLoopEndSample
            // 
            lblLoopEndSample.AutoSize = true;
            lblLoopEndSample.Location = new Point(102, 295); // Y: 275 -> 295
            lblLoopEndSample.Name = "lblLoopEndSample";
            lblLoopEndSample.Size = new Size(154, 24);
            lblLoopEndSample.TabIndex = 13;
            lblLoopEndSample.Text = "循环结束采样数：";
            // 
            // txtLoopEndSample
            // 
            txtLoopEndSample.Location = new Point(320, 290); // 顶部对齐
            txtLoopEndSample.Name = "txtLoopEndSample";
            txtLoopEndSample.ScrollBars = ScrollBars.Horizontal;
            txtLoopEndSample.Size = new Size(170, 30);
            txtLoopEndSample.TabIndex = 14;
            txtLoopEndSample.Text = "0"; // 默认0
            txtLoopEndSample.TextChanged += txtLoopEndSample_TextChanged;
            // 
            // lstPlaylist
            // 
            lstPlaylist.FormattingEnabled = true;
            lstPlaylist.ItemHeight = 24;
            lstPlaylist.Location = new Point(780, 32); // X: 620 -> 780
            lstPlaylist.Name = "lstPlaylist";
            lstPlaylist.Size = new Size(350, 412);
            lstPlaylist.TabIndex = 15;
            lstPlaylist.HorizontalScrollbar = true;
            lstPlaylist.DoubleClick += lstPlaylist_DoubleClick;
            // 
            // btnPrev
            // 
            btnPrev.Location = new Point(780, 450); // X: 620 -> 780
            btnPrev.Name = "btnPrev";
            btnPrev.Size = new Size(170, 40);
            btnPrev.TabIndex = 16;
            btnPrev.Text = "<< 上一首";
            btnPrev.UseVisualStyleBackColor = true;
            btnPrev.Click += btnPrev_Click;
            // 
            // btnNext
            // 
            btnNext.Location = new Point(960, 450); // X: 800 -> 960
            btnNext.Name = "btnNext";
            btnNext.Size = new Size(170, 40);
            btnNext.TabIndex = 17;
            btnNext.Text = "下一首 >>";
            btnNext.UseVisualStyleBackColor = true;
            btnNext.Click += btnNext_Click;
            // 
            // btnSwitchLang
            // 
            // 
            // btnSwitchLang
            // 
            btnSwitchLang.Location = new Point(20, 550); // Y: 500 -> 550
            btnSwitchLang.Name = "btnSwitchLang";
            btnSwitchLang.Size = new Size(100, 40);
            btnSwitchLang.TabIndex = 19;
            btnSwitchLang.Text = "English"; // 默认显示要切换到的语言
            btnSwitchLang.UseVisualStyleBackColor = true;
            btnSwitchLang.Click += btnSwitchLang_Click;
            
            // 
            // Start Fine-tune Buttons
            // 
            int btnBtnW = 72; int btnBtnH = 45; // 长度变为 45
            int btnX = 102; int btnGap = 75; 
            Font btnFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold); // 稍微加大加粗
            
            btnStartM1s.Location = new Point(btnX, 235); btnStartM1s.Size = new Size(btnBtnW, btnBtnH); 
            btnStartM1s.Text = "-1s"; btnStartM1s.Tag = "Start:-1.0"; btnStartM1s.Click += btnAdjust_Click;
            btnStartM1s.Font = btnFont;
            
            btnStartM05s.Location = new Point(btnX + btnGap, 235); btnStartM05s.Size = new Size(btnBtnW, btnBtnH);
            btnStartM05s.Text = "-0.5s"; btnStartM05s.Tag = "Start:-0.5"; btnStartM05s.Click += btnAdjust_Click;
            btnStartM05s.Font = btnFont;

            btnStartM500.Location = new Point(btnX + btnGap * 2, 235); btnStartM500.Size = new Size(btnBtnW, btnBtnH);
            btnStartM500.Text = "-500"; btnStartM500.Tag = "Start:-0"; btnStartM500.Click += btnAdjust_Click;
            btnStartM500.Font = btnFont;

            btnStartP500.Location = new Point(btnX + btnGap * 3, 235); btnStartP500.Size = new Size(btnBtnW, btnBtnH);
            btnStartP500.Text = "+500"; btnStartP500.Tag = "Start:0"; btnStartP500.Click += btnAdjust_Click;
            btnStartP500.Font = btnFont;

            btnStartP05s.Location = new Point(btnX + btnGap * 4, 235); btnStartP05s.Size = new Size(btnBtnW, btnBtnH);
            btnStartP05s.Text = "+0.5s"; btnStartP05s.Tag = "Start:0.5"; btnStartP05s.Click += btnAdjust_Click;
            btnStartP05s.Font = btnFont;

            btnStartP1s.Location = new Point(btnX + btnGap * 5, 235); btnStartP1s.Size = new Size(btnBtnW, btnBtnH);
            btnStartP1s.Text = "+1s"; btnStartP1s.Tag = "Start:1.0"; btnStartP1s.Click += btnAdjust_Click;
            btnStartP1s.Font = btnFont;

            // 
            // End Fine-tune Buttons
            // 
            btnEndM1s.Location = new Point(btnX, 335); btnEndM1s.Size = new Size(btnBtnW, btnBtnH);
            btnEndM1s.Text = "-1s"; btnEndM1s.Tag = "End:-1.0"; btnEndM1s.Click += btnAdjust_Click;
            btnEndM1s.Font = btnFont;

            btnEndM05s.Location = new Point(btnX + btnGap, 335); btnEndM05s.Size = new Size(btnBtnW, btnBtnH);
            btnEndM05s.Text = "-0.5s"; btnEndM05s.Tag = "End:-0.5"; btnEndM05s.Click += btnAdjust_Click;
            btnEndM05s.Font = btnFont;

            btnEndM500.Location = new Point(btnX + btnGap * 2, 335); btnEndM500.Size = new Size(btnBtnW, btnBtnH);
            btnEndM500.Text = "-500"; btnEndM500.Tag = "End:-0"; btnEndM500.Click += btnAdjust_Click;
            btnEndM500.Font = btnFont;

            btnEndP500.Location = new Point(btnX + btnGap * 3, 335); btnEndP500.Size = new Size(btnBtnW, btnBtnH);
            btnEndP500.Text = "+500"; btnEndP500.Tag = "End:0"; btnEndP500.Click += btnAdjust_Click;
            btnEndP500.Font = btnFont;

            btnEndP05s.Location = new Point(btnX + btnGap * 4, 335); btnEndP05s.Size = new Size(btnBtnW, btnBtnH);
            btnEndP05s.Text = "+0.5s"; btnEndP05s.Tag = "End:0.5"; btnEndP05s.Click += btnAdjust_Click;
            btnEndP05s.Font = btnFont;

            btnEndP1s.Location = new Point(btnX + btnGap * 5, 335); btnEndP1s.Size = new Size(btnBtnW, btnBtnH);
            btnEndP1s.Text = "+1s"; btnEndP1s.Tag = "End:1.0"; btnEndP1s.Click += btnAdjust_Click;
            btnEndP1s.Font = btnFont;
            
            // 
            // txtLoopStartSec
            // 
            txtLoopStartSec.Location = new Point(530, 192); // 同步上移
            txtLoopStartSec.Name = "txtLoopStartSec";
            txtLoopStartSec.Size = new Size(80, 30);
            txtLoopStartSec.TabIndex = 20;
            txtLoopStartSec.TextChanged += txtLoopStartSec_TextChanged;
            
            // 
            // txtLoopEndSec
            // 
            txtLoopEndSec.Location = new Point(530, 287); // 同步上移
            txtLoopEndSec.Name = "txtLoopEndSec";
            txtLoopEndSec.Size = new Size(80, 30);
            txtLoopEndSec.TabIndex = 21;
            txtLoopEndSec.TextChanged += txtLoopEndSec_TextChanged;
            
            // 
            // btnApplyLoop
            // 
            btnApplyLoop.Location = new Point(640, 187); // 右移但不遮挡列表
            btnApplyLoop.Name = "btnApplyLoop";
            btnApplyLoop.Size = new Size(130, 150);
            btnApplyLoop.TabIndex = 22;
            btnApplyLoop.Text = "确认应用\r\n并试听";
            btnApplyLoop.UseVisualStyleBackColor = true;
            btnApplyLoop.Click += btnApplyLoop_Click;



            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1150, 620); // 高度: 560 -> 620
            AllowDrop = false;
            Controls.Add(btnStartM1s); Controls.Add(btnStartM05s); Controls.Add(btnStartM500);
            Controls.Add(btnStartP500); Controls.Add(btnStartP05s); Controls.Add(btnStartP1s);
            Controls.Add(btnEndM1s); Controls.Add(btnEndM05s); Controls.Add(btnEndM500);
            Controls.Add(btnEndP500); Controls.Add(btnEndP05s);            Controls.Add(btnEndP1s);
            Controls.Add(txtLoopStartSec);
            Controls.Add(txtLoopEndSec);
            Controls.Add(btnApplyLoop);
            Controls.Add(btnNext);
            Controls.Add(btnPrev);
            Controls.Add(btnSwitchLang);
            Controls.Add(lstPlaylist);
            Controls.Add(lblAudioInfo);
            Controls.Add(lblLoopEndSample);
            Controls.Add(txtLoopEndSample);
            Controls.Add(lblStatus);
            Controls.Add(lblTime);
            Controls.Add(trkProgress);
            Controls.Add(trkVolume);
            Controls.Add(lblLoopSample);
            Controls.Add(txtLoopSample);
            Controls.Add(lblFilePath);
            Controls.Add(txtFilePath);
            Controls.Add(btnStop);
            Controls.Add(btnPause);
            Controls.Add(btnPlay);
            Controls.Add(btnSelectFile);
            Name = "Form1";
            Text = "无缝循环音乐播放器";
            ((System.ComponentModel.ISupportInitialize)trkVolume).EndInit();
            ((System.ComponentModel.ISupportInitialize)trkProgress).EndInit(); // 结束初始化
            ResumeLayout(false);
            PerformLayout();
            
            // 手动绑定事件 (防止 Designer 覆盖丢失) - 已移除拖拽事件
        }

        #endregion

        private Button btnSelectFile;
        private Button btnPlay;
        private Button btnPause;
        private Button btnStop;
        private TextBox txtFilePath;
        private Label lblFilePath;
        private Label lblLoopSample;
        private TextBox txtLoopSample;
        private TrackBar trkVolume;
        private Label lblStatus;
        private Label lblAudioInfo; // 声明新字段
        private System.Windows.Forms.Timer tmrUpdate;
        private TrackBar trkProgress;
        private Label lblTime;
        private Label lblLoopEndSample;
        private TextBox txtLoopEndSample;
        private ListBox lstPlaylist;
        private Button btnPrev;
        private Button btnNext;
        private Button btnSwitchLang;
        
        // 微调按钮 - 起始点
        private Button btnStartM1s;
        private Button btnStartM05s;
        private Button btnStartM500;
        private Button btnStartP500;
        private Button btnStartP05s;
        private Button btnStartP1s;
        
        // 微调按钮 - 结束点
        private Button btnEndM1s;
        private Button btnEndM05s;
        private Button btnEndM500;
        private Button btnEndP500;
        private Button btnEndP05s;
        private Button btnEndP1s;
        
        // 采样数转秒数显示与输入框
        private TextBox txtLoopStartSec;
        private TextBox txtLoopEndSec;
        private Button btnApplyLoop;
    }
}
