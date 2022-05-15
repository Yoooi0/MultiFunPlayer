namespace MultiFunPlayer.Common.Messages;

public class MediaDurationChangedMessage
{
    public TimeSpan? Duration { get; }
    public MediaDurationChangedMessage(TimeSpan? duration) => Duration = duration;
}
