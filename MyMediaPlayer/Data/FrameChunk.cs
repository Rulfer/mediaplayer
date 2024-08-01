using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMediaPlayer.Data
{
    internal class FrameChunk
    {
        public ConcurrentQueue<Bitmap> frameBuffer = new ConcurrentQueue<Bitmap>();
        public double id = 0;
    }
}
