using Stylet;

namespace MultiFunPlayer.VideoSource.MediaResource
{
    public interface IMediaResourceFactory
    {
        BindableCollection<IMediaPathModifier> PathModifiers { get; }
        MediaResourceInfo CreateFromPath(string path);
    }
}
