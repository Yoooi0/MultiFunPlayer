using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Plugin;
using Stylet;
using System.IO;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal class PluginViewModel : Screen, IDisposable
{
    private readonly IShortcutManager _shortcutManager;
    private FileSystemWatcher _watcher;

    public ObservableConcurrentDictionary<FileInfo, PluginContainer> Containers { get; }

    public PluginViewModel(IShortcutManager shortcutManager)
    {
        _shortcutManager = shortcutManager;

        Directory.CreateDirectory("Plugins");

        Containers = [];
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
        if (!Containers.TryGetValue(fileInfo, out var container))
            return;

        container.Dispose();
        Containers.Remove(fileInfo);

        UnregisterActions(_shortcutManager, container);
    }

    private void AddContainer(FileInfo fileInfo)
    {
        if (Containers.ContainsKey(fileInfo))
            return;

        var container = new PluginContainer(fileInfo);
        Containers.Add(fileInfo, container);
        container.Compile();

        RegisterActions(_shortcutManager, container);
    }

    private void RegisterActions(IShortcutManager s, PluginContainer container)
    {
        s.RegisterAction($"Plugin::{container.Name}::Start", () => container.Start());
        s.RegisterAction($"Plugin::{container.Name}::Stop", () => container.Stop());
    }

    private void UnregisterActions(IShortcutManager s, PluginContainer container)
    {
        s.UnregisterAction($"Plugin::{container.Name}::Start");
        s.UnregisterAction($"Plugin::{container.Name}::Stop");
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
