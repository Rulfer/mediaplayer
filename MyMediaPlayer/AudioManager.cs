using NAudio.Wave;
using System.Diagnostics;
using MyMediaPlayer.Extensions;

namespace MyMediaPlayer;

public class AudioManager
{
    private string VideoPath { get; }

    private WaveOutEvent _waveOut;
    private BufferedWaveProvider _bufferedWave;
    private Process _process;

    private bool _preProcessDone = false;

    private Thread _myThread;
    
    public AudioManager(string videoPath)
    {
        VideoPath = videoPath;
        
        VideoPlayer.Instance.OnPauseMedia += Pause;
        VideoPlayer.Instance.OnResumeMedia += Start;
    }

    private void Start()
    {
        Console.WriteLine("Resuming audio playback...");
        _waveOut.Play();
        _process.Resume();
        
    }

    private void Pause()
    {
        Console.WriteLine("Pause audio playback...");
        _waveOut.Pause();
        _bufferedWave.ClearBuffer(); // dump any queued samples
        _process.Suspend();
    }

    public async void StartAsync()
    {
        await RetrieveAudio();
    }

    private async Task RetrieveAudio()
    {
        try
        {
            // Start background reader loop
            await Task.Run(async () =>
            {
                var probeInfo = await ProbeAudioInformation(); // get channels & sample rate
                int channels = probeInfo.Channels;
                int sampleRate = probeInfo.SampleRate;
                int bitsPerSample = 16; // s16le

                string args = $"-re -i \"{VideoPath}\" -vn -ac {channels} -ar {sampleRate} -f s16le pipe:1";

                _process = new Process();
                _process.StartInfo = MyFFmpeg.NewProcessStartInfo(args);
                _process.EnableRaisingEvents = true;
                _process.Start();
                _ = MyFFmpeg.ReadStreamAsync(_process.StandardError.BaseStream, "ffmpeg-audio");

                _bufferedWave = new BufferedWaveProvider(new WaveFormat(sampleRate, bitsPerSample, channels))
                {
                    BufferDuration = TimeSpan.FromMilliseconds(200), // give it some room
                    DiscardOnBufferOverflow = true
                };

                _waveOut = new WaveOutEvent(); // safe high-level output
                _waveOut.Init(_bufferedWave);

                var buffer = new byte[16384];
                while (true)
                {
                    if (!VideoPlayer.Instance.IsMediaReady && _preProcessDone)
                    {
                        continue;
                    }

                    var bytesRead = await _process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                    if(!_preProcessDone)
                    {
                        _preProcessDone = true;
                        VideoPlayer.Instance.AudioReady = true;
                        if (bytesRead <= 0)
                        {
                            throw new NotImplementedException("Failed to read audio from ffmpeg.");
                        }
                    }
                    
                    if(bytesRead > 0)
                    {
                        _bufferedWave.AddSamples(buffer, 0, bytesRead);
                    }
                    else
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

    private async Task<(int Channels, int SampleRate)> ProbeAudioInformation()
    {
        string probeArgs =
            $"-i \"{VideoPath}\" -hide_banner -select_streams a:0 -show_entries stream=channels,sample_rate -of default=noprint_wrappers=1:nokey=0";
        using (var process = new Process
                   { StartInfo = MyFFmpeg.NewProcessStartInfo(probeArgs), EnableRaisingEvents = true })
        {
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Assign default values if the output is empty.
            int channels = 2;
            int sampleRate = 48000;

            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("channels=")) channels = int.Parse(line.Split('=')[1]);
                if (line.StartsWith("sample_rate=")) sampleRate = int.Parse(line.Split('=')[1]);
            }

            return (channels, sampleRate);
        }
    }

    // [DllImport("winmm.dll", SetLastError = true)]
    // private static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceID,
    //     ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);
    //
    // [DllImport("winmm.dll", SetLastError = true)]
    // private static extern int waveOutPrepareHeader(IntPtr hWaveOut,
    //     ref WAVEHDR lpWaveOutHdr, uint uSize);
    //
    // [DllImport("winmm.dll", SetLastError = true)]
    // private static extern int waveOutWrite(IntPtr hWaveOut,
    //     ref WAVEHDR lpWaveOutHdr, uint uSize);
    //
    // [DllImport("winmm.dll", SetLastError = true)]
    // private static extern int waveOutUnprepareHeader(IntPtr hWaveOut,
    //     ref WAVEHDR lpWaveOutHdr, uint uSize);
    //
    // [DllImport("winmm.dll", SetLastError = true)]
    // private static extern int waveOutClose(IntPtr hWaveOut);
    //
    // [StructLayout(LayoutKind.Sequential)]
    // private struct WAVEFORMATEX
    // {
    //     public ushort wFormatTag;
    //     public ushort nChannels;
    //     public uint nSamplesPerSec;
    //     public uint nAvgBytesPerSec;
    //     public ushort nBlockAlign;
    //     public ushort wBitsPerSample;
    //     public ushort cbSize;
    // }
    //
    // [StructLayout(LayoutKind.Sequential)]
    // private struct WAVEHDR
    // {
    //     public IntPtr lpData;
    //     public uint dwBufferLength;
    //     public uint dwBytesRecorded;
    //     public IntPtr dwUser;
    //     public uint dwFlags;
    //     public uint dwLoops;
    //     public IntPtr lpNext;
    //     public IntPtr reserved;
    // }
    //
    // private const int WAVE_FORMAT_PCM = 1;
    // private const int CALLBACK_NULL = 0;
}