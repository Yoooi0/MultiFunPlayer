namespace MultiFunPlayer.Common.Messages;

public class VideoPositionMessage
{
    public TimeSpan? Position { get; }
    public VideoPositionMessage(TimeSpan? position) => Position = position;
}
