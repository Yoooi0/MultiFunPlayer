namespace MultiFunPlayer.Common.Messages
{
    public class VideoFileChangedMessage
    {
        public string Path { get; }

        public VideoFileChangedMessage(string path) => Path = path;
    }
}
