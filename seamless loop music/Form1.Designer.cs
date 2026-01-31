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
            btnStop = new Button();
            txtFilePath = new TextBox();
            lblFilePath = new Label();
            lblLoopSample = new Label();
            txtLoopSample = new TextBox();
            trkVolume = new TrackBar();
            lblStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)trkVolume).BeginInit();
            SuspendLayout();
            // 
            // btnSelectFile
            // 
            btnSelectFile.Location = new Point(70, 32);
            btnSelectFile.Name = "btnSelectFile";
            btnSelectFile.Size = new Size(112, 34);
            btnSelectFile.TabIndex = 0;
            btnSelectFile.Text = "选择音频文件";
            btnSelectFile.UseVisualStyleBackColor = true;
            btnSelectFile.Click += btnSelectFile_Click;
            // 
            // btnPlay
            // 
            btnPlay.Enabled = false;
            btnPlay.Location = new Point(233, 32);
            btnPlay.Name = "btnPlay";
            btnPlay.Size = new Size(112, 34);
            btnPlay.TabIndex = 1;
            btnPlay.Text = "播放";
            btnPlay.UseVisualStyleBackColor = true;
            btnPlay.Click += btnPlay_Click;
            // 
            // btnStop
            // 
            btnStop.Enabled = false;
            btnStop.Location = new Point(392, 32);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(112, 34);
            btnStop.TabIndex = 2;
            btnStop.Text = "停止";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // txtFilePath
            // 
            txtFilePath.Location = new Point(208, 102);
            txtFilePath.Name = "txtFilePath";
            txtFilePath.ScrollBars = ScrollBars.Horizontal;
            txtFilePath.Size = new Size(276, 30);
            txtFilePath.TabIndex = 3;
            // 
            // lblFilePath
            // 
            lblFilePath.AutoSize = true;
            lblFilePath.Location = new Point(102, 105);
            lblFilePath.Name = "lblFilePath";
            lblFilePath.Size = new Size(100, 24);
            lblFilePath.TabIndex = 4;
            lblFilePath.Text = "\t音频路径：";
            // 
            // lblLoopSample
            // 
            lblLoopSample.AutoSize = true;
            lblLoopSample.Location = new Point(102, 160);
            lblLoopSample.Name = "lblLoopSample";
            lblLoopSample.Size = new Size(154, 24);
            lblLoopSample.TabIndex = 6;
            lblLoopSample.Text = "循环起始采样数：";
            // 
            // txtLoopSample
            // 
            txtLoopSample.Location = new Point(265, 157);
            txtLoopSample.Name = "txtLoopSample";
            txtLoopSample.ScrollBars = ScrollBars.Horizontal;
            txtLoopSample.Size = new Size(170, 30);
            txtLoopSample.TabIndex = 5;
            txtLoopSample.TextChanged += txtLoopSample_TextChanged;
            // 
            // trkVolume
            // 
            trkVolume.Location = new Point(141, 211);
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
            lblStatus.Location = new Point(233, 272);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(46, 24);
            lblStatus.TabIndex = 8;
            lblStatus.Text = "就绪";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(578, 344);
            Controls.Add(lblStatus);
            Controls.Add(trkVolume);
            Controls.Add(lblLoopSample);
            Controls.Add(txtLoopSample);
            Controls.Add(lblFilePath);
            Controls.Add(txtFilePath);
            Controls.Add(btnStop);
            Controls.Add(btnPlay);
            Controls.Add(btnSelectFile);
            Name = "Form1";
            Text = "无缝循环音乐播放器";
            ((System.ComponentModel.ISupportInitialize)trkVolume).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnSelectFile;
        private Button btnPlay;
        private Button btnStop;
        private TextBox txtFilePath;
        private Label lblFilePath;
        private Label lblLoopSample;
        private TextBox txtLoopSample;
        private TrackBar trkVolume;
        private Label lblStatus;
    }
}
