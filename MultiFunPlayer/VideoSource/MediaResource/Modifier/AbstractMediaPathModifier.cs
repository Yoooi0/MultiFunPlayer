using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.VideoSource.MediaResource.Modifier;

public abstract class AbstractMediaPathModifier : PropertyChangedBase, IMediaPathModifier
{
    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    public abstract string Description { get; }

    public abstract bool Process(ref string path);
}
