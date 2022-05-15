namespace MultiFunPlayer.Common.Messages;

public class MediaPositionChangedMessage
{
    public TimeSpan? Position { get; }
    public MediaPositionChangedMessage(TimeSpan? position) => Position = position;
}
