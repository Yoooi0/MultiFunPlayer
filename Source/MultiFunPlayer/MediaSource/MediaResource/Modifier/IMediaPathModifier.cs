namespace MultiFunPlayer.MediaSource.MediaResource.Modifier;

internal interface IMediaPathModifier
{
    string Name { get; }
    string Description { get; }

    string Process(string path);
}
