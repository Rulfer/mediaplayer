namespace MyMediaPlayer
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
            videoView = new LibVLCSharp.WinForms.VideoView();
            pictureBox = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)videoView).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox).BeginInit();
            SuspendLayout();
            // 
            // videoView
            // 
            videoView.BackColor = Color.Black;
            videoView.Location = new Point(12, 20);
            videoView.MediaPlayer = null;
            videoView.Name = "videoView";
            videoView.Size = new Size(304, 132);
            videoView.TabIndex = 0;
            videoView.Text = "videoView1";
            // 
            // pictureBox
            // 
            pictureBox.Location = new Point(176, 175);
            pictureBox.Name = "pictureBox";
            pictureBox.Size = new Size(379, 189);
            pictureBox.TabIndex = 1;
            pictureBox.TabStop = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(pictureBox);
            Controls.Add(videoView);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)videoView).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private LibVLCSharp.WinForms.VideoView videoView;
        internal PictureBox pictureBox;
    }
}
