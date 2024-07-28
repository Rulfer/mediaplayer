using LibVLCSharp.WinForms;

namespace MyMediaPlayer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void InitializePictureBox()
        {
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        }

        public VideoView GetMediaPlayer()
        {
            return videoView;
        }

        public void SetNewImage(Image image)
        {
            Invoke(new Action(() =>
            {
                pictureBox.Image?.Dispose();
                Program.Form.pictureBox.Image = image;
            }));
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
