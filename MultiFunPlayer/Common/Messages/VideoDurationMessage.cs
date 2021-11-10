namespace MultiFunPlayer.Common.Messages;

public class VideoDurationMessage
{
    public TimeSpan? Duration { get; }
    public VideoDurationMessage(TimeSpan? duration) => Duration = duration;
}
