using System.IO;

namespace MultiFunPlayer.Common
{
    public class VideoFileChangedMessage
    {
        public FileInfo VideoFile { get; }

        public VideoFileChangedMessage(FileInfo videoFile)
        {
            VideoFile = videoFile;
        }
    }
}
