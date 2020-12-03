using System;

namespace MultiFunPlayer.Common
{
    public class VideoPositionMessage
    {
        public TimeSpan? Position { get; }
        public VideoPositionMessage(TimeSpan? position) => Position = position;
    }
}
