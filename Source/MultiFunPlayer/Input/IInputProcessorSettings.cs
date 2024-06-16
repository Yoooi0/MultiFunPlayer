using Newtonsoft.Json;
using PropertyChanged;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.Input;

internal interface IInputProcessorSettings
{
    string Name { get; }
}

[AddINotifyPropertyChangedInterface]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal abstract partial class AbstractInputProcessorSettings : IInputProcessorSettings
{
    public string Name { get; init; }

    protected AbstractInputProcessorSettings()
    {
        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    }
}