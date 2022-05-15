namespace MultiFunPlayer.Common.Messages;

public class MediaSpeedChangedMessage
{
    public float Speed { get; }
    public MediaSpeedChangedMessage(float speed) => Speed = speed;
}
