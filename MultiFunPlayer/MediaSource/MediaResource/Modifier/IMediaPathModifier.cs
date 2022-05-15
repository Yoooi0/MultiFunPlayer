namespace MultiFunPlayer.MediaSource.MediaResource.Modifier;

public interface IMediaPathModifier
{
    string Name { get; }
    string Description { get; }

    bool Process(ref string path);
}
