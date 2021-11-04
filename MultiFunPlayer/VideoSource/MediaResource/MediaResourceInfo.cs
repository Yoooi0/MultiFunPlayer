namespace MultiFunPlayer.VideoSource.MediaResource
{
    public class MediaResourceInfo
    {
        public bool IsPath { get; init; }
        public bool IsUrl { get; init; }
        public bool IsUnc { get; init; }

        public bool Local { get; init; }
        public bool Remote => !Local;

        public bool IsModified { get; init; }
        public string ModifiedPath { get; init; }

        public string OriginalPath { get; init; }
        public string Source { get; init; }
        public string Name { get; init; }
    }
}
