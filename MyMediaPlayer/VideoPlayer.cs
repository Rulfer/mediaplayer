using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using LibVLCSharp.Shared;
using Xabe.FFmpeg;

namespace MyMediaPlayer
{
    internal class VideoPlayer
    {
        //private LibVLC _libVLC;
        //private MediaPlayer _mediaPlayer;

        //// Import SetDllDirectory from kernel32.dll
        //[DllImport("kernel32.dll", SetLastError = true)]
        //private static extern bool SetDllDirectory(string lpPathName);

        private ConcurrentQueue<Bitmap> _frameBuffer = new ConcurrentQueue<Bitmap>();
        private System.Threading.Timer _extractionTimer;
        private System.Threading.Timer _playbackTimer;

        private int _bufferSeconds = 10;
        private double _fps;
        private double _currentPosition = 0;

        private string _videoPath;

        private IMediaInfo _mediaInfo;
        private IVideoStream _videoStream;

        CancellationTokenSource _token;

        /// <summary>
        /// Start a video with a hardcoded path.
        /// </summary>
        internal async void Initialize()
        {
            _videoPath = @"C:\Users\rosse\Videos\Dungeoncrawler\Dungeoncrawler 2023.02.11 - 12.35.45.04.DVR.mp4";
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            Debug.WriteLine($"FFmpeg path is {ffmpegPath}, which {(Directory.Exists(ffmpegPath) ? "exists" : "doesn't exist")}.");
            FFmpeg.SetExecutablesPath(ffmpegPath);


            _mediaInfo = await FFmpeg.GetMediaInfo(_videoPath);
            _videoStream = _mediaInfo.VideoStreams.FirstOrDefault();

            if (_videoStream == null)
            {
                MessageBox.Show("No video stream found in the file.");
                return;
            }

            _fps = _videoStream.Framerate;
            _token = new CancellationTokenSource();

            Task.Run(() => RetrieveFrames(_token.Token));
            Task.Run(() => DisplayNextFrame(_token.Token));

            // This was given to me by ChatGPT.
            //var conversion = FFmpeg.Conversions.New()
            //    .AddParameter($"-i {videoPath}")
            //    .AddParameter($"-vf fps={fps} -f image2pipe -vcodec jpeg -");

            // This was given to me by https://www.baeldung.com/linux/ffmpeg-extract-video-frames
            //var test = FFmpeg.Conversions.New()
            //    .AddParameter($"ffmpeg -i {videoPath} -ss 00:00:05 -r 10 -s 640x360 -q:v 2 -vframes 25 frame%03d.jpg");

            //string destinationDir = @"C:\Users\rosse\Videos\Test";
            //if (!Directory.Exists(destinationDir))
            //    Directory.CreateDirectory(destinationDir);

            //Func<string, string> outputFileNameBuilder = (i) => $@"{destinationDir}\frame_{i:D3}.png";
            //IConversion conversion = (IConversion)await FFmpeg.Conversions.New()
            //                                    .AddStream(videoStream)
            //                                    .SetSeek(TimeSpan.FromSeconds(_currentPosition))
            //                                    .SetInputTime(TimeSpan.FromSeconds(_bufferSeconds))
            //                                    .ExtractEveryNthFrame(1, outputFileNameBuilder)
            //                                    .Start();
        }

        private async Task ExtractFrames()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Debug.WriteLine("Add to path: " + tempDir);
            if(!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);

            string outputFilePattern = Path.Combine(tempDir, "frame_%03d.png");

            // Extract frames for the next 10 seconds
            await FFmpeg.Conversions.New()
                .AddStream(_videoStream)
                .SetSeek(TimeSpan.FromSeconds(_currentPosition))
                .SetOutputTime(TimeSpan.FromSeconds(_bufferSeconds))
                .SetOutput(outputFilePattern)
                .Start();

            var frameFiles = Directory.GetFiles(tempDir, "*.png");
            foreach (var frameFile in frameFiles)
            {
                _frameBuffer.Enqueue(new Bitmap(frameFile));
                File.Delete(frameFile);
            }

            Directory.Delete(tempDir, true);

            // Update currentPosition to the end of the extracted segment
            _currentPosition += _bufferSeconds;
        }

        private async Task DisplayNextFrame(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_bufferSeconds * 1000);

                if (_frameBuffer.TryDequeue(out Bitmap frame))
                {
                    Program.Form.SetNewImage(frame);
                }
            }
        }

        private async Task RetrieveFrames(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await ExtractFrames();

                while (_frameBuffer.Count() / _fps > 5)
                    await Task.Yield();
            }
        }

        //private void DisplayNextFrame()
        //{
        //    if (_frameBuffer.TryDequeue(out Bitmap frame))
        //    {
        //        Program.Form.SetNewImage(frame);
        //    }
        //}

        //private void StartPlayback()
        //{
        //    _playbackTimer = new System.Threading.Timer(async _ => await ExtractFrames(), null, 0, _bufferSeconds * 1000);
        //}


        private Image ResizeImageToFit(Image image)
        {
            int sourceWidth = image.Width;
            int sourceHeight = image.Height;
            int targetWidth = Program.Frame.ClientSize.Width;
            int targetHeight = Program.Frame.ClientSize.Height;

            float nPercentW = (float)targetWidth / (float)sourceWidth;
            float nPercentH = (float)targetHeight / (float)sourceHeight;
            float nPercent = Math.Min(nPercentW, nPercentH);

            int destWidth = (int)(sourceWidth * nPercent);
            int destHeight = (int)(sourceHeight * nPercent);

            Bitmap result = new Bitmap(targetWidth, targetHeight);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Black);
                g.DrawImage(image, (targetWidth - destWidth) / 2, (targetHeight - destHeight) / 2, destWidth, destHeight);
            }
            return result;
        }
        //        internal void Initialize()
        //        {
        //            string WorkingDirectory;
        //#if DEBUG
        //            WorkingDirectory = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName;
        //#else
        //            WorkingDirectory = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
        //#endif

        //            // Copy libvlc files to output directory
        //            var libvlcDirectory = Path.Combine(WorkingDirectory, "libvlc");

        //            foreach (string s in Directory.GetFiles(libvlcDirectory))
        //                Debug.WriteLine("File: " + s);
        //            SetDllDirectory(libvlcDirectory);
        //            // Initialize the VLC library
        //            Core.Initialize(libvlcDirectory);

        //            _libVLC = new LibVLC();
        //            _mediaPlayer = new MediaPlayer(_libVLC);

        //            // Create the video view and add it to the form
        //            var videoView = new VideoView
        //            {
        //                MediaPlayer = _mediaPlayer,
        //                Dock = DockStyle.Fill
        //            };

        //            Program.Form.Controls.Add(videoView);
        //            StartDemo();
        //        }

        //        public void StartDemo()
        //        {
        //            var media = new Media(_libVLC, new Uri(@"C:\Users\rosse\Videos\Hunt  Showdown\Hunt  Showdown 2024.03.15 - 22.20.27.03.DVR.mp4"));

        //            _mediaPlayer.Play(media);

        //            // Add Play button
        //            var playButton = new Button
        //            {
        //                Text = "Play",
        //                Dock = DockStyle.Top
        //            };
        //            playButton.Click += (s, args) => _mediaPlayer.Play();
        //            Program.Form.Controls.Add(playButton);

        //            // Add Pause button
        //            var pauseButton = new Button
        //            {
        //                Text = "Pause",
        //                Dock = DockStyle.Top
        //            };
        //            pauseButton.Click += (s, args) => _mediaPlayer.Pause();
        //            Program.Form.Controls.Add(pauseButton);
        //        }

    }
}
