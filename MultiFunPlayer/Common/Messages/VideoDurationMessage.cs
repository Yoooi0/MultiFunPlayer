using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common
{
    public class VideoDurationMessage
    {
        public TimeSpan? Duration { get; }
        public VideoDurationMessage(TimeSpan? duration) => Duration = duration;
    }
}
