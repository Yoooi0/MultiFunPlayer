namespace MultiFunPlayer.Common.Messages;

public class MediaSpeedChangedMessage
{
    public double Speed { get; }
    public MediaSpeedChangedMessage(double speed) => Speed = speed;
}
