using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using LibVLCSharp.Shared;
using MyMediaPlayer.Data;
using Xabe.FFmpeg;

namespace MyMediaPlayer
{
    internal class VideoPlayer
    {
        internal static VideoPlayer Instance;
        
        private float _bufferSeconds = 0.5f;
        private int _fps;
        private double _videoDuration;
        private double _currentPosition = 0;

        private string _videoPath;
        private string _currentTempDir;

        private IMediaInfo _mediaInfo;
        private IVideoStream _videoStream;
        private AudioManager _audioManager;

        private volatile bool _audioReady = false;
        private volatile bool _videoReady = false;
        public bool IsMediaReady => !ShouldBePaused && AudioReady && VideoReady;

        public delegate void OnPauseMediaEventHandler();
        public delegate void OnResumeMediaEventHandler();

        public event OnPauseMediaEventHandler? OnPauseMedia;
        public event OnResumeMediaEventHandler? OnResumeMedia;

        public bool AudioReady
        {
            get => _audioReady;
            set
            {
                _audioReady = value;
                if(AudioReady && VideoReady && !ShouldBePaused)
                {
                    OnResumeMedia?.Invoke();
                }
            }
        }

        public bool VideoReady
        {
            get => _videoReady;
            set
            {
                _videoReady = value;
                if(AudioReady && VideoReady && !ShouldBePaused)
                {
                    OnResumeMedia?.Invoke();
                }
            }
        }

        public bool ShouldBePaused { get; set; } = false;
        // {
        //     get => _shouldBePaused;
        //     set
        //     {
        //         _shouldBePaused = value;
        //         if(ShouldBePaused)
        //             OnPauseMedia?.Invoke();
        //         else if(AudioReady && VideoReady && !ShouldBePaused)
        //         {
        //             OnResumeMedia?.Invoke();
        //         }
        //     }
        // }

        /// <summary>
        /// Start a video with a hardcoded path.
        /// </summary>
        internal void Initialize()
        {
            Instance = this;
            
            _videoPath = @"C:\Users\rosse\Downloads\bbb_sunflower_2160p_30fps_normal.mp4";
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            Debug.WriteLine($"FFmpeg path is {ffmpegPath}, which {(Directory.Exists(ffmpegPath) ? "exists" : "doesn't exist")}.");

            MyFFmpeg.VideoPath = _videoPath;
            _fps = MyFFmpeg.GetFPS();
            _videoDuration = MyFFmpeg.GetVideoDuration();
            Debug.WriteLine("Yo, MyFPS is " + _fps);

            _currentTempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Debug.WriteLine($"Temp files stored in '{_currentTempDir}'.");

            MyFFmpeg.GetAllTheFrames();
            
            _audioManager  = new AudioManager(_videoPath);
            _audioManager.StartAsync();
        }

        internal void PausePlayHotkeyPressed()
        {
            ShouldBePaused = !ShouldBePaused;
            if(ShouldBePaused)
                OnPauseMedia?.Invoke();
            else
                OnResumeMedia?.Invoke();
        }

        internal void Seek(double seconds)
        {
            
        }
    }
}
