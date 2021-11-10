namespace MultiFunPlayer.Common.Messages;

public class VideoSeekMessage
{
    public TimeSpan? Position { get; }
    public VideoSeekMessage(TimeSpan? position) => Position = position;
}
