namespace MultiFunPlayer.Common.Messages;

public class MediaPositionChangedMessage
{
    public TimeSpan? Position { get; }
    public bool ForceSeek { get; }

    public MediaPositionChangedMessage(TimeSpan? position, bool forceSeek = false)
    {
        Position = position;
        ForceSeek = forceSeek;
    }
}
