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
            btnApplyLoop = new Button(); // 新增确认按钮
            btnSwitchLang = new Button(); // 语言切换按钮
            ((System.ComponentModel.ISupportInitialize)trkVolume).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trkProgress).BeginInit(); // 别忘了这个
            SuspendLayout();
            // 
            // btnSelectFile
            // 
            btnSelectFile.Location = new Point(50, 30);
            btnSelectFile.Name = "btnSelectFile";
            btnSelectFile.Size = new Size(160, 50);
            btnSelectFile.TabIndex = 0;
            btnSelectFile.Text = "导入音乐文件夹";
            btnSelectFile.UseVisualStyleBackColor = true;
            btnSelectFile.Click += btnSelectFile_Click;
            // 
            // btnPlay
            // 
            btnPlay.Enabled = false;
            btnPlay.Location = new Point(230, 32);
            btnPlay.Name = "btnPlay";
            btnPlay.Size = new Size(112, 50);
            btnPlay.TabIndex = 1;
            btnPlay.Text = "播放";
            btnPlay.UseVisualStyleBackColor = true;
            btnPlay.Click += btnPlay_Click;
            // 
            // btnPause
            // 
            btnPause.Enabled = false;
            btnPause.Location = new Point(360, 32);
            btnPause.Name = "btnPause";
            btnPause.Size = new Size(112, 50);
            btnPause.TabIndex = 10;
            btnPause.Text = "暂停";
            btnPause.UseVisualStyleBackColor = true;
            btnPause.Click += btnPause_Click;
            // 
            // btnStop
            // 
            btnStop.Enabled = false;
            btnStop.Location = new Point(490, 32);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(112, 34);
            btnStop.TabIndex = 2;
            btnStop.Text = "停止";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // txtFilePath
            // 
            txtFilePath.Location = new Point(208, 100);
            txtFilePath.Name = "txtFilePath";
            txtFilePath.ScrollBars = ScrollBars.Horizontal;
            txtFilePath.Size = new Size(276, 30);
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
            txtLoopSample.Location = new Point(265, 190); // Y=190
            txtLoopSample.Name = "txtLoopSample";
            txtLoopSample.ScrollBars = ScrollBars.Horizontal;
            txtLoopSample.Size = new Size(170, 30);
            txtLoopSample.TabIndex = 5;
            txtLoopSample.TextChanged += txtLoopSample_TextChanged;
            // 
            // trkVolume
            // 
            trkVolume.Location = new Point(141, 360); // Y=360
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
            lblStatus.Location = new Point(233, 430); // Y=430
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(46, 24);
            lblStatus.TabIndex = 8;
            lblStatus.Text = "就绪";
            // 
            // lblAudioInfo
            // 
            lblAudioInfo.AutoSize = true;
            lblAudioInfo.Location = new Point(102, 145); // Y=145
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
            trkProgress.Location = new Point(141, 285); // Y=285
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
            lblTime.Location = new Point(497, 290); // Y=290
            lblTime.Name = "lblTime";
            lblTime.Size = new Size(59, 24);
            lblTime.TabIndex = 12;
            lblTime.Text = "00:00";
            // 
            // lblLoopEndSample
            // 
            lblLoopEndSample.AutoSize = true;
            lblLoopEndSample.Location = new Point(102, 240); // Y=240
            lblLoopEndSample.Name = "lblLoopEndSample";
            lblLoopEndSample.Size = new Size(154, 24);
            lblLoopEndSample.TabIndex = 13;
            lblLoopEndSample.Text = "循环结束采样数：";
            // 
            // txtLoopEndSample
            // 
            txtLoopEndSample.Location = new Point(265, 237); // Y=237
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
            lstPlaylist.Location = new Point(620, 32);
            lstPlaylist.Name = "lstPlaylist";
            lstPlaylist.Size = new Size(350, 412);
            lstPlaylist.TabIndex = 15;
            lstPlaylist.HorizontalScrollbar = true;
            lstPlaylist.DoubleClick += lstPlaylist_DoubleClick;
            // 
            // btnPrev
            // 
            btnPrev.Location = new Point(620, 450);
            btnPrev.Name = "btnPrev";
            btnPrev.Size = new Size(170, 40);
            btnPrev.TabIndex = 16;
            btnPrev.Text = "<< 上一首";
            btnPrev.UseVisualStyleBackColor = true;
            btnPrev.Click += btnPrev_Click;
            // 
            // btnNext
            // 
            btnNext.Location = new Point(800, 450);
            btnNext.Name = "btnNext";
            btnNext.Size = new Size(170, 40);
            btnNext.TabIndex = 17;
            btnNext.Text = "下一首 >>";
            btnNext.UseVisualStyleBackColor = true;
            btnNext.Click += btnNext_Click;
            // 
            // btnApplyLoop
            // 
            btnApplyLoop.Enabled = false; // 默认灰色
            btnApplyLoop.Location = new Point(450, 190); // 放在输入框右侧
            btnApplyLoop.Name = "btnApplyLoop";
            btnApplyLoop.Size = new Size(100, 77); // 高度覆盖两个输入框
            btnApplyLoop.TabIndex = 18;
            btnApplyLoop.Text = "确认\n设置";
            btnApplyLoop.UseVisualStyleBackColor = true;
            btnApplyLoop.Click += btnApplyLoop_Click;
            // 
            // btnSwitchLang
            // 
            btnSwitchLang.Location = new Point(20, 440);
            btnSwitchLang.Name = "btnSwitchLang";
            btnSwitchLang.Size = new Size(100, 40);
            btnSwitchLang.TabIndex = 19;
            btnSwitchLang.Text = "English"; // 默认显示要切换到的语言
            btnSwitchLang.UseVisualStyleBackColor = true;
            btnSwitchLang.Click += btnSwitchLang_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 500); 
            AllowDrop = false;
            Controls.Add(btnNext);
            Controls.Add(btnPrev);
            Controls.Add(btnApplyLoop); // 添加到窗体
            Controls.Add(btnSwitchLang);
            Controls.Add(lstPlaylist);
            Controls.Add(lblAudioInfo); // 补回来！！！
            Controls.Add(lblLoopEndSample);
            Controls.Add(txtLoopEndSample);
            Controls.Add(lblStatus);
            Controls.Add(lblTime); // 添加时间标签
            Controls.Add(trkProgress); // 添加进度条
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
        private Button btnApplyLoop;
        private Button btnSwitchLang;
    }
}
