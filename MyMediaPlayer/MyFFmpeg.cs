using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using Xabe.FFmpeg;

namespace MyMediaPlayer
{
    internal static class MyFFmpeg
    {
        private static string _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        private static string _ffmpegEXE = Path.Combine(_ffmpegPath, "ffmpeg.exe");
        private static string _ffprobeEXE = Path.Combine(_ffmpegPath, "ffprobe.exe");
        private static string _ffplayEXE = Path.Combine(_ffmpegPath, "ffplay.exe");

        private static string _videoPath;

        private static Process _processExtractFrame = null;
        private static Process _processExtractAudio = null;

        /// <summary>
        /// Actual frame rate of the current video.
        /// </summary>
        private static int CACHED_FPS;

        private static double CACHED_VIDEO_DURATION;

        private static readonly object _frameLock = new object();
        private static List<byte[]> _frameCache = new List<byte[]>();
        private static Thread _frameThread;

        private static bool _audioReady = false;
        private static bool _videoReady = false;
        private static AudioManager _audioManager;

        public static bool AudioReady
        {
            get => _audioReady;
            set
            {
                _audioReady = value;
                if (AudioReady && VideoReady)
                {
                    _audioManager.Start();
                }
            }
        }

        public static bool VideoReady
        {
            get => _videoReady;
            set
            {
                _videoReady = value;
                if (AudioReady && VideoReady)
                {
                    _audioManager.Start();
                }
            }
        }
        public static bool MediaReady => AudioReady && VideoReady;
        public enum Extract
        {
            Frame,
            Audio
        }

        internal static string VideoPath
        {
            get { return _videoPath; }
            set { _videoPath = "\"" + value + "\""; }
        }

        private static void CloseProcess()
        {
            _processExtractFrame?.Close();
            _processExtractAudio?.Close();
            _processExtractFrame = null;
            _processExtractAudio = null;
        }

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceID,
            ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut,
            ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutWrite(IntPtr hWaveOut,
            ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut,
            ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        private const int WAVE_FORMAT_PCM = 1;
        private const int CALLBACK_NULL = 0;

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

        /// <summary>
        /// Extract frames directly from the ffmpeg output, and convert them to Image from byte[].
        /// </summary>
        internal static async void GetAllTheFrames()
        {
            //int coreCount = Math.Clamp(Environment.ProcessorCount - 2, 1, Environment.ProcessorCount);

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

            // With these arguments I don't ever get to cache frames. I extract the current frame, and then process it. I then wait until the next frame before processing that one.
            // string singleFrameArgument = $@"-hwaccel auto -re -ss 00:00:00 -i {VideoPath} -map 0:v -preset ultrafast -s {width}x{height} -threads {4} -vf fps={CACHED_FPS} -f image2pipe -vcodec rawvideo -pix_fmt bgr24 pipe:1";
            string singleFrameArgument =
                $@"-hwaccel cuda -re -ss 00:00:00 -i {VideoPath} -map 0:v -preset ultrafast -s {width}x{height} -threads {4} -vf fps={CACHED_FPS} -f image2pipe -vcodec rawvideo -pix_fmt bgr24 pipe:1";

            _audioManager = new AudioManager(VideoPath);
            _ = _audioManager.StartAsync();
            try
            {
                await Task.Run(async () =>
                {
                    // _ = audioManager.StartAsync();
                    
                    using (var process = new Process
                               { StartInfo = NewProcessStartInfo(singleFrameArgument), EnableRaisingEvents = true })
                    {
                        Console.WriteLine("Start reading frames");
                        process.Start();

                        var errorTask = ReadStreamAsync(process.StandardError.BaseStream, "my-ffmpeg-error");

                        // Calculate the frame size in bytes (3 bytes per pixel for BGR format)
                        int frameSize = width * height * 3; // 3 bytes for BGR format

                        var buffer = new byte[frameSize];
                        var stream = process.StandardOutput.BaseStream;
                        int bytesRead = 0;

                        while (true)
                        {
                            if(!MediaReady && VideoReady)
                                continue;
                            
                            bytesRead += await stream.ReadAsync(buffer, bytesRead, frameSize - bytesRead);
                            VideoReady = true;

                            if (bytesRead == frameSize)
                            {
                                // Process the full frame

                                // lock(_frameLock)
                                // {
                                //     _frameCache.Add(buffer);
                                // }
                                ProcessFrame(buffer, width, height);
                                bytesRead = 0; // Reset for the next frame
                            }
                            else if (bytesRead == 0) // No more data
                            {
                                break;
                            }
                        }

                        Console.WriteLine("Data read.");


                        process.WaitForExit();
                    }
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e.StackTrace);
                Console.WriteLine(e.Message);
            }
        }

        // private const int WHDR_DONE = 0x00000001;
        // private static async Task StreamAudio()
        // {
        //     var probeInfo = await ProbeAudioInformation();
        //     int channels = probeInfo.Channels;
        //     int sampleRate = probeInfo.SampleRate;
        //     int bitsPerSample = 16;
        //
        //     string args = $"-re -i \"{VideoPath}\" -vn -ac {channels} -ar {sampleRate} -f s16le pipe:1";
        //
        //     using var process = new Process { StartInfo = NewProcessStartInfo(args), EnableRaisingEvents = true };
        //     process.Start();
        //     var stream = process.StandardOutput.BaseStream;
        //     _ = ReadStreamAsync(process.StandardError.BaseStream, "ffmpeg-audio");
        //
        //     var format = new WAVEFORMATEX
        //     {
        //         wFormatTag = WAVE_FORMAT_PCM,
        //         nChannels = (ushort)channels,
        //         nSamplesPerSec = (uint)sampleRate,
        //         wBitsPerSample = (ushort)bitsPerSample,
        //         nBlockAlign = (ushort)((channels * bitsPerSample) / 8),
        //         nAvgBytesPerSec = (uint)(sampleRate * ((channels * bitsPerSample) / 8)),
        //         cbSize = 0
        //     };
        //
        //     int res = waveOutOpen(out IntPtr hWaveOut, 0, ref format, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
        //     if (res != 0) throw new Exception("waveOutOpen failed: " + res);
        //
        //     
        //     int bufferSize = 32768; // 32 KB buffer (~0.3s stereo 48kHz)
        //     IntPtr ptr = Marshal.AllocHGlobal(bufferSize);
        //     var header = new WAVEHDR
        //     {
        //         lpData = ptr,
        //         dwBufferLength = (uint)bufferSize,
        //         dwFlags = 0,
        //         dwLoops = 0
        //     };
        //     waveOutPrepareHeader(hWaveOut, ref header, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
        //     
        //     const int BufferSize = 16384;
        //     byte[] buffer = new byte[BufferSize];
        //     
        //     try
        //     {
        //         while (true)
        //         {
        //             int bytesRead = await stream.ReadAsync(buffer, 0, BufferSize);
        //             if (bytesRead == 0) break;
        //
        //             // Wait until previous buffer is done
        //             while ((header.dwFlags & (uint)WHDR_DONE) == 0)
        //                 await Task.Delay(1);
        //
        //             // Copy data into pre-allocated memory
        //             Marshal.Copy(buffer, 0, ptr, bytesRead);
        //
        //             header.lpData = ptr;
        //             header.dwBufferLength = (uint)bytesRead;
        //             header.dwFlags &= ~(uint)WHDR_DONE;
        //
        //             waveOutPrepareHeader(hWaveOut, ref header, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
        //             waveOutWrite(hWaveOut, ref header, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
        //         }
        //     }
        //     finally
        //     {
        //         Marshal.FreeHGlobal(ptr);
        //         waveOutClose(hWaveOut);
        //     }
        //     
        //     waveOutClose(hWaveOut);
        //     process.WaitForExit();
        // }
        //
        // private static async Task<(int Channels, int SampleRate)> ProbeAudioInformation()
        // {
        //     string probeArgs =
        //         $"-i \"{VideoPath}\" -hide_banner -select_streams a:0 -show_entries stream=channels,sample_rate -of default=noprint_wrappers=1:nokey=0";
        //     using var process = new Process
        //         { StartInfo = NewProcessStartInfo(probeArgs), EnableRaisingEvents = true };
        //     process.Start();
        //
        //     var output = await process.StandardOutput.ReadToEndAsync();
        //     await process.WaitForExitAsync();
        //
        //     // Assign default values if the output is empty.
        //     int channels = 2;
        //     int sampleRate = 48000;
        //
        //     foreach (var line in output.Split('\n'))
        //     {
        //         if (line.StartsWith("channels=")) channels = int.Parse(line.Split('=')[1]);
        //         if (line.StartsWith("sample_rate=")) sampleRate = int.Parse(line.Split('=')[1]);
        //     }
        //
        //     return (channels, sampleRate);
        // }
        

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
                    //g.SmoothingMode = SmoothingMode.None;         // Disable smoothing for speed
                    //g.PixelOffsetMode = PixelOffsetMode.HighSpeed; // Optimize pixel offset handling

                    g.Clear(Color.Black);
                    g.DrawImage(image, (targetWidth - destWidth) / 2, (targetHeight - destHeight) / 2, destWidth,
                        destHeight);
                }

                return result;
            }
            
        }
    }