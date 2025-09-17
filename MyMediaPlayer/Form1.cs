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
            this.KeyPreview = true;
            this.AllowDrop = true;
            this.DragDrop += OnDragDrop;
            this.KeyDown += OnKeyDown;
        }

        /// <summary>
        /// Element dropped into the media controller.
        /// </summary>
        private void OnDragDrop(object? sender, DragEventArgs e)
        {
            
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyValue == (int)Keys.Space)
            {
                VideoPlayer.Instance.PausePlayHotkeyPressed();
            }
        }

        /// <summary>
        /// Update what frame is currently being displayed.
        /// </summary>
        /// <param name="image"></param>
        public void SetNewImage(Image image)
        {
            Invoke(new Action(() =>
            {
                pictureBox.Image?.Dispose();
                pictureBox.Image = image;
                pictureBox.Refresh();
            }));
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Console.WriteLine("Form loaded.");
        }
    }
}
