namespace MultiFunPlayer.MediaSource.MediaResource;

internal interface IMediaResourceInfoBuilder
{
    IMediaResourceInfoBuilder WithOriginalPath(string originalPath);
    IMediaResourceInfoBuilder WithSourceAndName(string source, string name);
    IMediaResourceInfoBuilder AsPath();
    IMediaResourceInfoBuilder AsUrl();
    IMediaResourceInfoBuilder AsLocal();
    IMediaResourceInfoBuilder AsModified(string modifiedPath);

    MediaResourceInfo Build();
}

internal class MediaResourceInfoBuilder : IMediaResourceInfoBuilder
{
    [Flags]
    private enum BuilderFlags
    {
        None = 0,
        IsPath = 1 << 0,
        IsUrl = 1 << 1,
        IsLocal = 1 << 2,
        Modified = 1 << 3
    }

    private string _source;
    private string _name;
    private string _originalPath;
    private string _modifiedPath;
    private BuilderFlags _flags;

    public IMediaResourceInfoBuilder AsPath()
    {
        _flags |= BuilderFlags.IsPath;
        return this;
    }

    public IMediaResourceInfoBuilder AsUrl()
    {
        _flags |= BuilderFlags.IsUrl;
        return this;
    }

    public IMediaResourceInfoBuilder AsLocal()
    {
        _flags |= BuilderFlags.IsLocal;
        return this;
    }

    public IMediaResourceInfoBuilder AsModified(string modifiedPath)
    {
        _flags |= BuilderFlags.Modified;
        _modifiedPath = modifiedPath;
        return this;
    }

    public IMediaResourceInfoBuilder WithOriginalPath(string originalPath)
    {
        _originalPath = originalPath;
        return this;
    }

    public IMediaResourceInfoBuilder WithSourceAndName(string source, string name)
    {
        _source = source;
        _name = name;
        return this;
    }

    public MediaResourceInfo Build()
        => new()
        {
            IsPath = _flags.HasFlag(BuilderFlags.IsPath),
            IsUrl = _flags.HasFlag(BuilderFlags.IsUrl),
            IsModified = _flags.HasFlag(BuilderFlags.Modified),
            Local = _flags.HasFlag(BuilderFlags.IsLocal),
            OriginalPath = _originalPath,
            ModifiedPath = _modifiedPath,
            Source = _source,
            Name = _name
        };
}
