using System;

namespace MultiFunPlayer.VideoSource.MediaResource
{
    public interface IMediaResourceInfoBuilder
    {
        IMediaResourceInfoBuilder WithOriginalPath(string originalPath);
        IMediaResourceInfoBuilder WithSourceAndName(string source, string name);
        IMediaResourceInfoBuilder AsPath(bool local);
        IMediaResourceInfoBuilder AsUrl(bool local);
        IMediaResourceInfoBuilder AsUnc();
        IMediaResourceInfoBuilder AsModified(string modifiedPath);

        MediaResourceInfo Build();
    }

    public class MediaResourceInfoBuilder : IMediaResourceInfoBuilder
    {
        [Flags]
        private enum BuilderFlags
        {
            IsPath = 1 << 0,
            IsUrl = 1 << 1,
            IsUnc = 1 << 2,
            IsLocal = 1 << 3,
            Modified = 1 << 4
        }

        private string _source;
        private string _name;
        private string _originalPath;
        private string _modifiedPath;
        private BuilderFlags _flags;

        public IMediaResourceInfoBuilder AsPath(bool local)
        {
            _flags &= (BuilderFlags)~3;
            _flags |= BuilderFlags.IsPath | (BuilderFlags)((int)BuilderFlags.IsLocal * (local ? 1 : 0));
            return this;
        }

        public IMediaResourceInfoBuilder AsUnc()
        {
            _flags = BuilderFlags.IsUnc | BuilderFlags.IsLocal;
            return this;
        }

        public IMediaResourceInfoBuilder AsUrl(bool local)
        {
            _flags &= (BuilderFlags)~3;
            _flags |= BuilderFlags.IsUrl | (BuilderFlags)((int)BuilderFlags.IsLocal * (local ? 1 : 0));
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
            => new MediaResourceInfo()
            {
                IsPath = _flags.HasFlag(BuilderFlags.IsPath),
                IsUnc = _flags.HasFlag(BuilderFlags.IsUnc),
                IsUrl = _flags.HasFlag(BuilderFlags.IsUrl),
                IsModified = _flags.HasFlag(BuilderFlags.Modified),
                Local = _flags.HasFlag(BuilderFlags.IsLocal),
                OriginalPath = _originalPath,
                ModifiedPath = _modifiedPath,
                Source = _source,
                Name = _name
            };
    }
}
