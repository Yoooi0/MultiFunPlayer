using System;

namespace MultiFunPlayer.Common
{
    public class VideoSeekMessage
    {
        public TimeSpan? Position { get; }
        public VideoSeekMessage(TimeSpan? position) => Position = position;
    }
}
