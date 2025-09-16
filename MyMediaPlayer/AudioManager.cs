using NAudio.Wave;
using System.Diagnostics;

namespace MyMediaPlayer;

public class AudioManager
{
    private string VideoPath { get; set; }
    
    private WaveOutEvent _waveOut;
    private BufferedWaveProvider _bufferedWave;
    private Process _process;
    
    public AudioManager(string videoPath)
    {
        VideoPath = videoPath;
    }

    public void Start()
    {
        _waveOut.Play();
    }

    public void Pause()
    {
        
    }

    public async Task StartAsync()
    {
        var probeInfo = await ProbeAudioInformation(); // get channels & sample rate
        int channels = probeInfo.Channels;
        int sampleRate = probeInfo.SampleRate;
        int bitsPerSample = 16; // s16le

        string args = $"-re -i \"{VideoPath}\" -vn -ac {channels} -ar {sampleRate} -f s16le pipe:1";

        
        _process = new Process
        {
            StartInfo = MyFFmpeg.NewProcessStartInfo(args),
            EnableRaisingEvents = true
        };
        _process.Start();
        _ =MyFFmpeg.ReadStreamAsync(_process.StandardError.BaseStream, "ffmpeg-audio");
        
        // await using var waveStream = new RawSourceWaveStream(
        //     process.StandardOutput.BaseStream,
        //     new WaveFormat(sampleRate, bitsPerSample, channels));
        _bufferedWave = new BufferedWaveProvider(new WaveFormat(sampleRate, bitsPerSample, channels))
        {
            BufferDuration = TimeSpan.FromSeconds(5), // give it some room
            DiscardOnBufferOverflow = true
        };
        
        _waveOut = new WaveOutEvent(); // safe high-level output
        _waveOut.Init(_bufferedWave);

        Console.WriteLine("Audio playing...");

        // Start background reader loop
        await Task.Run(async () =>
        {
            var buffer = new byte[16384];
            int bytesRead;
            while (true)
            {
                if(!MyFFmpeg.MediaReady && MyFFmpeg.AudioReady)
                    continue;
                
                while ((bytesRead = await _process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    _bufferedWave.AddSamples(buffer, 0, bytesRead);
                    MyFFmpeg.AudioReady = true;
                }
            }
            
        });
        _process.WaitForExit();

    }
    
    private async Task<(int Channels, int SampleRate)> ProbeAudioInformation()
    {
        string probeArgs = $"-i \"{VideoPath}\" -hide_banner -select_streams a:0 -show_entries stream=channels,sample_rate -of default=noprint_wrappers=1:nokey=0";
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

}