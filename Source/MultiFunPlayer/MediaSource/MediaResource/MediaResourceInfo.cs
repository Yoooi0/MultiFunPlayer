namespace MultiFunPlayer.MediaSource.MediaResource;

public enum MediaResourcePathType
{
    Url,
    File
}

public sealed class MediaResourceInfo
{
    public MediaResourcePathType PathType { get; init; }

    public string ModifiedPath { get; init; }
    public string OriginalPath { get; init; }

    public string Source { get; init; }
    public string Name { get; init; }

    public bool IsFile => PathType == MediaResourcePathType.File;
    public bool IsUrl => PathType == MediaResourcePathType.Url;
    public bool IsModified => ModifiedPath != null;
    public string Path => IsModified ? ModifiedPath : OriginalPath;
}
