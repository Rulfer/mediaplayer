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

        internal static string VideoPath { get; set; } = string.Empty;

        /// <summary>
        /// Remember to set <see cref="VideoPath"/> as that is what we use when retrieving the FPS.
        /// </summary>
        internal static double GetFPS()
        {
            string fps = string.Empty;
            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobeEXE,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of csv=p=0 \"{VideoPath}\"",
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
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegEXE,
                //Arguments = $"-ss 00:00:00 -i \"{VideoPath}\" -t 10 -vf fps={desiredFPS} \"{outputFolder}\\frame_%04d.png\"",
                Arguments = $"-ss {from} -i \"{VideoPath}\" -t {duration} -vf fps={desiredFPS} \"{outputFolder}\\frame_%04d.png\"",
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
    }
}
