using LibVLCSharp.WinForms;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyMediaPlayer
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);


        public Form1()
        {
            InitializeComponent();
        }

        private void InitializePictureBox()
        {
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        }

        public void SetNewImage(Image image)
        {
            Invoke(new Action(() =>
            {
                pictureBox.Image?.Dispose();
                pictureBox.Image = image;
                pictureBox.Refresh();
            }));
        }

        internal void InjectFFmpeg(Process process)
        {
            Invoke(new Action(() =>
            {
                // The two DLL's are from this forum post: https://stackoverflow.com/questions/31465630/ffplay-successfully-moved-inside-my-winform-how-to-set-it-borderless"

                // child, new parent
                // make 'this' the parent of ffmpeg (presuming you are in scope of a Form or Control)
                SetParent(process.MainWindowHandle, this.Handle);

                // window, x, y, width, height, repaint
                // move the ffplayer window to the top-left corner and set the size to 320x280
                MoveWindow(process.MainWindowHandle, 0, 0, 320, 280, true);
            }));
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void pictureBox_Click(object sender, EventArgs e)
        {

        }
    }
}
