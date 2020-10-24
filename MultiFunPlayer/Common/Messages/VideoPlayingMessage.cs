namespace MultiFunPlayer.Common
{
    public class VideoPlayingMessage
    {
        public bool IsPlaying { get; }
        public VideoPlayingMessage(bool isPlaying) => IsPlaying = isPlaying;
    }
}
