using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MediaSource.MediaResource.Modifier;

internal abstract class AbstractMediaPathModifier : PropertyChangedBase, IMediaPathModifier
{
    public string Name { get; init; }

    public abstract string Process(string path);

    protected AbstractMediaPathModifier()
    {
        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    }
}
