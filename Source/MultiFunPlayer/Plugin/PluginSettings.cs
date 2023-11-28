using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using Stylet;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace MultiFunPlayer.Plugin;

public abstract class PluginSettingsBase : PropertyChangedBase
{
    protected UIElement CreateViewFromStream(Stream stream) => XamlReader.Load(stream) as UIElement;
    protected UIElement CreateViewFromFile(string path) => CreateViewFromStream(File.OpenRead(path));
    protected UIElement CreateViewFromString(string xamlContent)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xamlContent));
        return CreateViewFromStream(stream);
    }

    public virtual UIElement CreateView() => null;
    public abstract void HandleSettings(JObject settings, SettingsAction action);
}