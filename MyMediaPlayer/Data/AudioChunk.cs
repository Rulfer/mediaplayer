using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMediaPlayer.Data
{
    internal class AudioChunk
    {
        /// <summary>
        /// How many seconds each wav file should be.
        /// </summary>
        public static float Intervals = 1.0f;

        public double id = 0;
        public MemoryStream audio;
    }
}
