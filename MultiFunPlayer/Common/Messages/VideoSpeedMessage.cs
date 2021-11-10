namespace MultiFunPlayer.Common.Messages;

public class VideoSpeedMessage
{
    public float Speed { get; }
    public VideoSpeedMessage(float speed) => Speed = speed;
}
