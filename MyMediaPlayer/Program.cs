using System.Media;
using System.Security.Cryptography;
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

            // Example of easy way to play an audio file.
            //LoadFileAsync();
        }

        static async void LoadFileAsync()
        {
            byte[] bytes = await File.ReadAllBytesAsync(@"path/to/some/audio.wav");
            MemoryStream memoryStream = new MemoryStream(bytes);

            memoryStream.Position = 0; // Reset the stream position to the beginning
            using (SoundPlayer soundPlayer = new SoundPlayer(memoryStream))
            {
                soundPlayer.Play(); // Play the audio asynchronously
            }
        }
    }
}