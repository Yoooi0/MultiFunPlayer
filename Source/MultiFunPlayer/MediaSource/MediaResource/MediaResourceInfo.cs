namespace MultiFunPlayer.MediaSource.MediaResource;

public enum MediaResourcePathType
{
    Uri,
    File
}

public sealed class MediaResourceInfo
{
    public MediaResourcePathType PathType { get; init; }

    public string ModifiedPath { get; init; }
    public string OriginalPath { get; init; }

    public string Source { get; init; }
    public string Name { get; init; }

    public bool IsModified => ModifiedPath != null;
    public string Path => IsModified ? ModifiedPath : OriginalPath;
}
