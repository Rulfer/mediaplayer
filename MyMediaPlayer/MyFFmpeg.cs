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

        private static Process _process = null;

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
            if (_process == null)
                return;

            _process.Close();
            _process = null;
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
            return first / second;
        }

        /// <summary>
        /// Remember to set <see cref="VideoPath"/> as that is what we use when retrieving the FPS.
        /// </summary>
        internal static async Task ExtractFrames(string outputFolder, int desiredFPS, string from = "00:00:00", float duration = 0.5f)
        {
            CloseProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegEXE,
                //Arguments = $"-ss 00:00:00 -i \"{VideoPath}\" -t 10 -vf fps={desiredFPS} \"{outputFolder}\\frame_%04d.png\"",
                Arguments = $"-ss {from} -i {VideoPath} -t {duration} -vf fps={desiredFPS} \"{outputFolder}\\frame_%04d.png\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            await Task.Run(() =>
            {
                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                }
            });
        }

        internal static async Task PlayVideo()
        {
            CloseProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = $@"{ffplayEXE}",
                Arguments = "-noborder -an -x 100 -y 100 -i " + VideoPath,
                RedirectStandardOutput = true,
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
                using (_process = Process.Start(startInfo))
                {
                    _process.EnableRaisingEvents = true;
                    _process.OutputDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay-data");
                    _process.OutputDataReceived += FfplayProcess_OutputDataReceived;
                    _process.ErrorDataReceived += (o, e) => Debug.WriteLine(e.Data ?? "NULL", "ffplay-error");
                    _process.Exited += (o, e) => Debug.WriteLine("Exited: " + e.ToString(), "ffplay-exit");
                    _process.BeginOutputReadLine();
                    PollForPlaybackStart();
                    //await Task.Delay(500);
                    //Program.Form.InjectFFmpeg(ffplay);

                    _process.WaitForExit();
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
                    if (_process != null && !_process.HasExited)
                    {
                        // Check if the process is using CPU
                        _process.Refresh();
                        if (_process.TotalProcessorTime.TotalMilliseconds > 0)
                        {
                            playbackStarted = true;
                            await Task.Delay(500); // Ensure enough time for playback to start
                            Program.Form.InjectFFmpeg(_process);
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
