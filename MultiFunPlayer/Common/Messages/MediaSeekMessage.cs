namespace MultiFunPlayer.Common.Messages;

public class MediaSeekMessage
{
    public TimeSpan? Position { get; }
    public MediaSeekMessage(TimeSpan? position) => Position = position;
}
