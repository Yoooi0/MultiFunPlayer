using System.IO;

namespace MultiFunPlayer.Common
{
    public class VideoFileChangedMessage
    {
        public FileInfo VideoFile { get; }
        public VideoFileChangedMessage(string path) => VideoFile = File.Exists(path) ? new FileInfo(path) : null;
    }
}
