using System;

namespace MultiFunPlayer.Common
{
    public class VideoDurationMessage
    {
        public TimeSpan? Duration { get; }
        public VideoDurationMessage(TimeSpan? duration) => Duration = duration;
    }
}
