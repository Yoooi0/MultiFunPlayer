using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common
{
    public class VideoSpeedMessage
    {
        public float Speed { get; }
        public VideoSpeedMessage(float speed) => Speed = speed;
    }
}
