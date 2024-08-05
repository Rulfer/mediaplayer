using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyMediaPlayer
{
    internal static class MyFFmpeg
    {
        private static string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        private static string ffmpegEXE = Path.Combine(ffmpegPath, "ffmpeg.exe");
        private static string ffprobeEXE = Path.Combine(ffmpegPath, "ffprobe.exe");
        private static string ffplayEXE = Path.Combine(ffmpegPath, "ffplay.exe");

        private static string _videoPath;

        private static Process _processExtractFrame = null;
        private static Process _processExtractAudio = null;

        private static int CACHED_FPS;
        private static double CACHED_VIDEO_DURATION;

        public enum Extract
        {
            Frame,
            Audio
        }

        internal static string VideoPath
        {
            get
            {
                return _videoPath;
            }
            set
            {
                _videoPath = "\"" + value + "\"";
            }
        }

        private static void CloseProcess()
        {
            _processExtractFrame?.Close();
            _processExtractAudio?.Close();
            _processExtractFrame = null;
            _processExtractAudio = null;
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
                FileName = ffprobeEXE,
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
                FileName = ffprobeEXE,
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

        private static ProcessStartInfo NewProcessStartInfo(string arguments)
        {
            return new ProcessStartInfo
            {
                FileName = ffmpegEXE,
                Arguments = $"{arguments}",

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true

            };
        }

        private static void StartProcess(this Process process, ProcessStartInfo startInfo)
        {
            process = Process.Start(startInfo);
            process.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-data");
            process.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-error");
            process.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffmpeg-exit");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        //private static string BuildFFmpegArguments(string uniqueArguments, string from = "00:00:00", float duration = 0.5f)
        private static string BuildFFmpegArguments(string uniqueArguments, TimeSpan from, float duration)
        {
            //return $@"-threads 4 -ss {from} -t {duration} -i {VideoPath} {uniqueArguments}";
            return $@"-hwaccel cuda -hwaccel_output_format cuda -ss {from} -t {duration} -i {VideoPath} {uniqueArguments}";
        }



        internal static async Task<System.Collections.Concurrent.ConcurrentQueue<Bitmap>> GetNextFrame(float interval, TimeSpan from, float duration, int width = 1920, int height = 1080)
        {
            Debug.WriteLine("GetNextFrame");
            //string argument = $@"-i {VideoPath} -ss {from} -vf fps={1 / interval} -t {duration} -f image2pipe -pix_fmt rgb24 -vcodec rawvideo pipe:1";
            // Reducing the amount of used threads slows down the process, but increases the stability of the computer in general.
            int coreCount = Math.Clamp(Environment.ProcessorCount - 2, 1, Environment.ProcessorCount); 
            string argument = $@"-hwaccel auto -i {VideoPath} -threads {coreCount} -ss {from} -vf fps={1 / interval} -t {duration} -f image2pipe -pix_fmt rgb24 pipe:1";
            System.Collections.Concurrent.ConcurrentQueue<Bitmap> queue = new System.Collections.Concurrent.ConcurrentQueue<Bitmap>();

            try
            {
                await Task.Run(async () =>
                {
                    using (var process = new Process { StartInfo = NewProcessStartInfo(argument), EnableRaisingEvents = true })
                    {
                        //process.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-data");
                        //process.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-error");
                        //process.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffmpeg-exit");
                        process.Start();

                        var errorTask = ReadStreamAsync(process.StandardError.BaseStream, "my-ffmpeg-error");

                        //process.BeginOutputReadLine();
                        //process.BeginErrorReadLine();

                        int frameSize = width * height * 3; // Example for 640x480 RGB frames


                        Debug.WriteLine("Start reading frames");
                        int expectedFrames = (int)(from.TotalSeconds / interval);
                        Debug.WriteLine($"Expecting {expectedFrames} frames");
                        List<byte[]> frames = new List<byte[]>();
                        for (int i = 0; i < expectedFrames; i++)
                        {
                            byte[] buffer = new byte[frameSize];
                            int bytesRead = 0;

                            while (bytesRead < frameSize)
                            {
                                int read = await process.StandardOutput.BaseStream.ReadAsync(buffer, bytesRead, frameSize - bytesRead);
                                if (read == 0)
                                {
                                    Debug.WriteLine("End of stream reached or no data available.");
                                    break;
                                }
                                bytesRead += read;
                            }

                            if (bytesRead == frameSize)
                            {
                                queue.Enqueue(ConvertToBitMap(buffer, width, height));
                                Debug.WriteLine("Added new frame to buffer");
                            }
                            else
                            {
                                Debug.WriteLine($"Incomplete frame read: {bytesRead} bytes read, expected {frameSize} bytes");
                                break;
                            }
                        }

                        process.WaitForExit(); // Ensure the process exits properly
                        await errorTask; // Ensure we read the error output completely
                    }
                });
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
                Debug.WriteLine(e.Message);
            }
            return queue;
        }

        private static async Task ReadStreamAsync(Stream stream, string tag)
        {
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    Debug.WriteLine(line ?? "NULL", tag);
                }
            }
        }

        private static Bitmap ConvertToBitMap(byte[] data, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            IntPtr p = bmpData.Scan0;
            System.Runtime.InteropServices.Marshal.Copy(data, 0, p, data.Length);
            bitmap.UnlockBits(bmpData);
            return bitmap;
        }

        //internal static async Task<System.Collections.Concurrent.ConcurrentQueue<Bitmap>> GetFrames(string outputFolder, int desiredFPS, string from = "00:00:00", float duration = 0.5f)
        internal static async Task<System.Collections.Concurrent.ConcurrentQueue<Bitmap>> GetFrames(string outputFolder, int desiredFPS, TimeSpan from, float duration)
        {
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                Process.Start("explorer.exe", $@"{outputFolder}");
            }

            string videoArguments = $"-vf fps={desiredFPS} -f image2 \"{outputFolder}\\frame_%04d.png\"";
            //string videoArguments = $"-vf fps={desiredFPS} -f image2pipe \"{outputFolder}\\frame_%04d.png\"";

            var processStartInfo = NewProcessStartInfo(BuildFFmpegArguments(videoArguments, from, duration));
            Debug.WriteLine(processStartInfo.Arguments, "GetFrames");
            await Task.Run(async () =>
            {
                using (var process = Process.Start(processStartInfo))
                {
                    process.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-data");
                    process.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-error");
                    process.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffmpeg-exit");

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    //using (var memoryStream = new MemoryStream())
                    //{
                    //    var buffer = new byte[4096];
                    //    int bytesRead;
                    //    while ((bytesRead = await process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    //    {
                    //        memoryStream.Write(buffer, 0, bytesRead);

                    //        // Check if we've got a full BMP image
                    //        if (IsBmpImage(memoryStream))
                    //        {
                    //            memoryStream.Position = 0;
                    //            var bitmap = new Bitmap(memoryStream);
                    //            memoryStream.SetLength(0); // Clear the stream for the next image

                    //            // Update the PictureBox with the new frame
                    //            Program.Form.SetNewImage(bitmap);
                    //        }
                    //    }
                    //}

                    //process.WaitForExit();
                }
            });

            System.Collections.Concurrent.ConcurrentQueue<Bitmap> frameBuffer = new System.Collections.Concurrent.ConcurrentQueue<Bitmap>();

            var frameFiles = Directory.GetFiles(outputFolder, "*.png");
            foreach (var frameFile in frameFiles)
            {
                try
                {
                    using (var stream = new FileStream(frameFile, FileMode.Open, FileAccess.Read))
                    {
                        var bitmap = new Bitmap(stream);
                        frameBuffer.Enqueue(bitmap);
                    }
                    File.Delete(frameFile);
                    Debug.WriteLine("Frame added to buffer and deleted: " + frameFile);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine("Error processing frame file: " + ex.Message);
                }
            }

            return frameBuffer;

        }

        private static bool IsBmpImage(MemoryStream stream)
        {
            if (stream.Length < 2)
                return false;

            var buffer = stream.GetBuffer();
            return buffer[0] == 'B' && buffer[1] == 'M';
        }

        /// <summary>
        /// Remember to set <see cref="VideoPath"/> as that is what we use when retrieving the FPS.
        /// This extract both frames and audio for the given time frame.
        /// </summary>
        /// <returns>
        /// Absolute path to the generated file(s)
        /// </returns>
        internal static async Task<string> ExtractData(Extract extract, string outputFolder, int desiredFPS, string from = "00:00:00", float duration = 0.5f)
        {
            CloseProcess();

            string pathToReturn = "NULL";

            string uniqueArguments = "";
            switch (extract)
            {
                case Extract.Frame:
                    uniqueArguments = $"-vf fps={desiredFPS} \"{outputFolder}\\frame_%04d.png\"";
                    break;

                case Extract.Audio:
                    string fileName = $"\"{outputFolder}\\output" + from.Replace(":", "_") + ".wav\"";
                    pathToReturn = Path.Combine(pathToReturn, fileName);
                    // Optinally add -ar 44100 to ensure the bitrate of the audio.
                    uniqueArguments = $"-vn -map 0:a:0 -acodec pcm_s16le {fileName}";
                    break;
            }

            string arguments = $"-ss {from} -t {duration} -i {VideoPath} {uniqueArguments}";


            await Task.Run(() =>
            {
                using (var process = Process.Start(NewProcessStartInfo(arguments)))
                {
                    process.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-data");
                    process.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffmpeg-error");
                    process.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffmpeg-exit");

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    process.WaitForExit();
                }
            });

            return pathToReturn;
        }

        #region FFPlay.exe
        /// <summary>
        /// Plays <see cref="VideoPath"/> using ffplay.exe instead of ffmpeg.exe
        /// </summary>
        /// <returns></returns>
        internal static async Task PlayVideo()
        {
            CloseProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = $@"{ffplayEXE}",
                Arguments = "-noborder -an -x 100 -y 100 -i " + VideoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };



            //ffplay.EnableRaisingEvents = true;
            //ffplay.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay");
            //ffplay.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay");
            //ffplay.Exited += (o, e) => Debug.WriteLine("Exited", "ffplay");

            //ffplay.EnableRaisingEvents = true;
            //ffplay.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay-data");
            //ffplay.OutputDataReceived += FfplayProcess_OutputDataReceived;
            //ffplay.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay-error");
            //ffplay.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffplay-exit");
            //ffplay.StartInfo = startInfo;
            //ffplay.Start();
            //ffplay.BeginOutputReadLine();
            await Task.Run(async () =>
            {
                using (_processExtractFrame = Process.Start(startInfo))
                {
                    _processExtractFrame.EnableRaisingEvents = true;
                    _processExtractFrame.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay-data");
                    _processExtractFrame.OutputDataReceived += FfplayProcess_OutputDataReceived;
                    _processExtractFrame.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay-error");
                    _processExtractFrame.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffplay-exit");
                    _processExtractFrame.BeginOutputReadLine();
                    PollForPlaybackStart();
                    //await Task.Delay(500);
                    //Program.Form.InjectFFmpeg(ffplay);

                    _processExtractFrame.WaitForExit();
                }
            });
        }

        private static async void PollForPlaybackStart()
        {
            bool playbackStarted = false;
            while (!playbackStarted)
            {
                await Task.Delay(10); // Poll every 10ms
                try
                {
                    if (_processExtractFrame != null && !_processExtractFrame.HasExited)
                    {
                        // Check if the process is using CPU
                        _processExtractFrame.Refresh();
                        if (_processExtractFrame.TotalProcessorTime.TotalMilliseconds > 0)
                        {
                            playbackStarted = true;
                            await Task.Delay(500); // Ensure enough time for playback to start
                            Program.Form.InjectFFmpeg(_processExtractFrame);
                            Debug.WriteLine("Detected activity in process");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle exceptions
                    MessageBox.Show($"Error while polling: {ex.Message}");
                }
            }
        }

        private static void FfplayProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && e.Data.Contains("Starting playback"))
            {
                Debug.WriteLine("Started playback", "ffplay-data");
                //playbackTimer.Start();
            }
        }
        #endregion
    }
}
