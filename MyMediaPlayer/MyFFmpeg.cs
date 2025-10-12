using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MyMediaPlayer.Extensions;

namespace MyMediaPlayer
{
    internal static class MyFFmpeg
    {
        private static string _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        private static string _ffmpegEXE = Path.Combine(_ffmpegPath, "ffmpeg.exe");
        private static string _ffprobeEXE = Path.Combine(_ffmpegPath, "ffprobe.exe");
        private static string _ffplayEXE = Path.Combine(_ffmpegPath, "ffplay.exe");

        private static string _videoPath;

        private static Process _process = null;

        /// <summary>
        /// Actual frame rate of the current video.
        /// </summary>
        private static int CACHED_FPS;

        private static double CACHED_VIDEO_DURATION;

        private static Thread _frameThread;

        internal static string VideoPath
        {
            get { return _videoPath; }
            set { _videoPath = "\"" + value + "\""; }
        }

        private static void CloseProcess()
        {
            _process?.Close();
            _process = null;
        }
        
        /// <summary>
        /// Remember to set <see cref="VideoPath"/> as that is what we use when retrieving the FPS.
        /// </summary>
        internal static int GetFPS()
        {
            CloseProcess();
            string fps = string.Empty;
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffprobeEXE,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of csv=p=0 {VideoPath}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                using (var reader = process.StandardOutput)
                {
                    fps = reader.ReadToEnd().Trim();
                }
            }

            string[] splits = fps.Split("/");
            double first = double.Parse(splits[0]);
            double second = double.Parse(splits[1]);
            CACHED_FPS = (int)Math.Round(first / second);
            return CACHED_FPS;
        }

        internal static double GetVideoDuration()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _ffprobeEXE,
                Arguments = $"-v error -show_entries format=duration -of csv=p=0 {VideoPath}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                using (StreamReader reader = process.StandardOutput)
                {
                    string result = reader.ReadToEnd();
                    CACHED_VIDEO_DURATION = double.Parse(result);
                    return double.Parse(result);
                }
            }
        }

        public static ProcessStartInfo NewProcessStartInfo(string arguments)
        {
            return new ProcessStartInfo
            {
                FileName = _ffmpegEXE,
                Arguments = $"{arguments}",

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        private static bool _preProcessDone = false;
        private static ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
        private static void Start()
        {
            Console.WriteLine("Resuming video playback...");
            _process.Resume();
            _pauseEvent.Set();    }

        private static void Pause()
        {
            Console.WriteLine("Pause video playback...");
            _process.Suspend();
            _pauseEvent.Reset();
            
        }

        /// <summary>
        /// Extract frames directly from the ffmpeg output, and convert them to Image from byte[].
        /// </summary>
        internal static async void GetAllTheFrames()
        {
            //int coreCount = Math.Clamp(Environment.ProcessorCount - 2, 1, Environment.ProcessorCount);

            VideoPlayer.Instance.OnPauseMedia += Pause;
            VideoPlayer.Instance.OnResumeMedia += Start;
            
            //await ExtractForLoop();
            await ExtractFPS();
        }

        /// <summary>
        /// Extract all frames in order, but restrict the speed using FPS.
        /// </summary>
        /// <returns></returns>
        private static async Task ExtractFPS()
        {
            // -loglevel quiet
            int width = 1920;
            int height = 1080;
            // Calculate the frame size in bytes (3 bytes per pixel for BGR format)
            int frameSize = width * height * 3; // 3 bytes for BGR format
            // With these arguments I don't ever get to cache frames. I extract the current frame, and then process it. I then wait until the next frame before processing that one.
            // string singleFrameArgument = $@"-hwaccel auto -re -ss 00:00:00 -i {VideoPath} -map 0:v -preset ultrafast -s {width}x{height} -threads {4} -vf fps={CACHED_FPS} -f image2pipe -vcodec rawvideo -pix_fmt bgr24 pipe:1";
            string singleFrameArgument =
                $@"-hwaccel cuda -re -ss 00:00:00 -i {VideoPath} -map 0:v -preset ultrafast -s {width}x{height} -threads {4} -vf fps={CACHED_FPS} -f image2pipe -vcodec rawvideo -pix_fmt bgr24 pipe:1";

            try
            {
                await Task.Run(async () =>
                {
                    _process = new Process();
                    _process.StartInfo = NewProcessStartInfo(singleFrameArgument);
                    _process.EnableRaisingEvents = true;
                    _process.Start();

                    _ = ReadStreamAsync(_process.StandardError.BaseStream, "my-ffmpeg-error");

                    var buffer = new byte[frameSize];
                    var stream = _process.StandardOutput.BaseStream;
                    int bytesRead = 0;

                    while (true)
                    {
                        _pauseEvent.Wait(); // blocks here if paused
                        
                        if (!VideoPlayer.Instance.IsMediaReady && _preProcessDone)
                        {
                            // The first frame has been extracted, and the media should be paused, so don't process any more frames for now. 
                            // TODO: Pause #Process since this runs in the background, handled by windows.
                            continue;
                        }

                        bytesRead += await stream.ReadAsync(buffer, bytesRead, frameSize - bytesRead);
                        if (!_preProcessDone)
                        {
                            VideoPlayer.Instance.VideoReady = true;
                            _preProcessDone = true;
                            if (bytesRead <= 0)
                            {
                                throw new NotImplementedException("Failed to read video from ffmpeg.");
                            }
                        }

                        if (bytesRead == frameSize)
                        {
                            // Process the full frame
                            ProcessFrame(buffer, width, height);
                            bytesRead = 0; // Reset for the next frame
                        }
                        else if (bytesRead == 0) // No more data
                        {
                            break;
                        }
                    }

                    _process.WaitForExit();
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e.StackTrace);
                Console.WriteLine(e.Message);
            }
        }


        public static async Task ReadStreamAsync(Stream stream, string tag)
        {
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    // Console.WriteLine(line ?? "NULL");
                }
            }
        }

        private static void ProcessFrame(byte[] frameData, int width, int height)
        {
            // Convert byte array to Bitmap or handle raw data as needed
            using (var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.WriteOnly, bmp.PixelFormat);

                // Copy the frame data to the bitmap
                Marshal.Copy(frameData, 0, bmpData.Scan0, frameData.Length);

                bmp.UnlockBits(bmpData);

                // Display the bitmap in a PictureBox, or use it in your Windows Forms application
                Program.Form.SetNewImage(ResizeImageToFit(bmp));
            }
        }

        private static Image ResizeImageToFit(Image image)
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
                // Optimize the graphics settings
                g.InterpolationMode = InterpolationMode.NearestNeighbor; // Prioritize speed over quality

                g.Clear(Color.Black);
                g.DrawImage(image, (targetWidth - destWidth) / 2, (targetHeight - destHeight) / 2, destWidth,
                    destHeight);
            }

            return result;
        }
    }
}