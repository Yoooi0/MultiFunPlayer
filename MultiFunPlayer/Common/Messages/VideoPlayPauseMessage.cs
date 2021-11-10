namespace MultiFunPlayer.Common.Messages;

public class VideoPlayPauseMessage
{
    public bool State { get; }
    public VideoPlayPauseMessage(bool state) => State = state;
}
