namespace MultiFunPlayer.Common.Messages;

public class MediaPathChangedMessage
{
    public string Path { get; }

    public MediaPathChangedMessage(string path) => Path = path;
}
