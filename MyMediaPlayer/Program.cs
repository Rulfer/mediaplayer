using Xabe.FFmpeg;

namespace MyMediaPlayer
{
    internal static class Program
    {
        internal static VideoPlayer VideoPlayer = new VideoPlayer();
        internal static Form1 Form;

        internal static PictureBox Frame => Form.pictureBox;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            Form = new Form1();
            Form.Load += OnFormLoaded;
            Application.Run(Form);

        }

        static void OnFormLoaded(object sender, EventArgs e)
        {
            VideoPlayer.Initialize();
        }
    }
}