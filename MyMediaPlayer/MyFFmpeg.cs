using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private static double CACHED_FPS;
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
        internal static double GetFPS()
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
            CACHED_FPS = first / second;
            return first / second;
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
            switch(extract)
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


            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegEXE,
                Arguments = $"{arguments}",

                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            await Task.Run(() =>
            {
                using (var process = Process.Start(startInfo))
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
    }
}
