namespace MultiFunPlayer.VideoSource.MediaResource
{
    public class MediaResourceInfo
    {
        public string Source { get; }
        public string Name { get; }

        public MediaResourceInfo(string source, string name)
        {
            Source = source;
            Name = name;
        }
    }
}
