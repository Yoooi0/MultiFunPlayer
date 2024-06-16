namespace MultiFunPlayer.MediaSource.MediaResource.Modifier;

internal interface IMediaPathModifier
{
    string Name { get; }

    string Process(string path);
}
