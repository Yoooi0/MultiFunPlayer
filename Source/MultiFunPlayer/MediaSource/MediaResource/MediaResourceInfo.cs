namespace MultiFunPlayer.MediaSource.MediaResource;

public enum MediaResourcePathType
{
    Url,
    File
}

public sealed record MediaResourceInfo(MediaResourcePathType PathType, string ModifiedPath, string OriginalPath, string Source, string Name)
{
    public bool IsFile => PathType == MediaResourcePathType.File;
    public bool IsUrl => PathType == MediaResourcePathType.Url;
    public bool IsModified => ModifiedPath != null;
    public string Path => IsModified ? ModifiedPath : OriginalPath;

    public bool Equals(MediaResourceInfo other) => other != null && string.Equals(Source, other.Source, StringComparison.Ordinal) && string.Equals(Name, other.Name, StringComparison.Ordinal);
    public override int GetHashCode() => HashCode.Combine(Source, Name);
}
