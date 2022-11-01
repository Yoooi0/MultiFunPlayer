using MultiFunPlayer.Common;
using MultiFunPlayer.Plugin;
using Stylet;
using System.IO;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class PluginViewModel : Screen, IHandle<SettingsMessage>, IDisposable
{
    private FileSystemWatcher _watcher;

    public ObservableConcurrentDictionary<FileInfo, PluginContainer> Containers { get; }

    public PluginViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);

        Directory.CreateDirectory("Plugins");

        Containers = new ObservableConcurrentDictionary<FileInfo, PluginContainer>();
        _watcher = new FileSystemWatcher()
        {
            Filter = "*.cs",
            Path = Path.Join(Directory.GetCurrentDirectory(), "Plugins"),
            EnableRaisingEvents = true
        };

        _watcher.Created += OnWatcherCreated;
        _watcher.Deleted += OnWatcherDeleted;

        foreach (var fileInfo in new DirectoryInfo("Plugins").SafeEnumerateFileSystemInfos("*.cs"))
            AddContainer(new FileInfo(fileInfo.FullName));
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        if (!File.Exists(e.FullPath))
            return;

        RemoveContainer(new FileInfo(e.FullPath));
    }

    private void OnWatcherCreated(object sender, FileSystemEventArgs e)
    {
        if (!File.Exists(e.FullPath))
            return;

        AddContainer(new FileInfo(e.FullPath));
    }

    private void RemoveContainer(FileInfo fileInfo)
    {
        if (!Containers.ContainsKey(fileInfo))
            return;

        Containers[fileInfo].Dispose();
        Containers.Remove(fileInfo);
    }

    private void AddContainer(FileInfo fileInfo)
    {
        if (Containers.ContainsKey(fileInfo))
            return;

        var container = new PluginContainer(fileInfo);
        Containers.Add(fileInfo, container);
        container.Compile();
    }

    public void Handle(SettingsMessage message)
    {
        foreach (var (_, container) in Containers)
            container.HandleSettings(message.Action);
    }

    protected virtual void Dispose(bool disposing)
    {
        _watcher?.Dispose();
        _watcher = null;

        foreach (var (_, container) in Containers)
            container.Dispose();

        Containers.Clear();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
