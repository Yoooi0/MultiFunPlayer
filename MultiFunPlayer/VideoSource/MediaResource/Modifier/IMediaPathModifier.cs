namespace MultiFunPlayer.VideoSource.MediaResource
{
    public interface IMediaPathModifier
    {
        string Name { get; }
        string Description { get; }

        bool Process(ref string path);
    }
}
