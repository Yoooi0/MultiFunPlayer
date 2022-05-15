namespace MultiFunPlayer.Common.Messages;

public class MediaPlayingChangedMessage
{
    public bool IsPlaying { get; }
    public MediaPlayingChangedMessage(bool isPlaying) => IsPlaying = isPlaying;
}
