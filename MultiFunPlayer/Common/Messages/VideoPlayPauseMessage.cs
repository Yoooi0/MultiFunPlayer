namespace MultiFunPlayer.Common
{
    public class VideoPlayPauseMessage
    {
        public bool State { get; }
        public VideoPlayPauseMessage(bool state) => State = state;
    }
}
