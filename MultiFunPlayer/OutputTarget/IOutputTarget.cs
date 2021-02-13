namespace MultiFunPlayer.OutputTarget
{
    public enum OutputTargetStatus
    {
        Disconnected,
        Disconnecting,
        Connecting,
        Connected
    }

    public interface IOutputTarget
    {
        string Name { get; }
        OutputTargetStatus Status { get; }
    }
}
