namespace MultiFunPlayer.Common.Messages;

public class MediaPlayPauseMessage
{
    public bool State { get; }
    public MediaPlayPauseMessage(bool state) => State = state;
}
