using MultiFunPlayer.Common;
using MultiFunPlayer.Plugin;
using MultiFunPlayer.Shortcut;
using Stylet;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class PluginViewModel : Screen, IDisposable
{
    private readonly IShortcutManager _shortcutManager;
    private FileSystemWatcher _watcher;

    public ObservableConcurrentDictionary<FileInfo, PluginContainer> Containers { get; }

    public PluginViewModel(IShortcutManager shortcutManager)
    {
        _shortcutManager = shortcutManager;

        Directory.CreateDirectory("Plugins");

        Containers = new ObservableConcurrentDictionary<FileInfo, PluginContainer>(new FileInfoFullNameComparer());
        _watcher = new FileSystemWatcher()
        {
            Filter = "*.cs",
            Path = Path.Join(Directory.GetCurrentDirectory(), "Plugins"),
            EnableRaisingEvents = true
        };

        _watcher.Created += OnWatcherCreated;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.Deleted += OnWatcherDeleted;

        foreach (var fileInfo in new DirectoryInfo("Plugins").SafeEnumerateFileSystemInfos("*.cs"))
            AddContainer(new FileInfo(fileInfo.FullName));
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        RemoveContainer(new FileInfo(e.OldFullPath));
        AddContainer(new FileInfo(e.FullPath));
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e) => RemoveContainer(new FileInfo(e.FullPath));
    private void OnWatcherCreated(object sender, FileSystemEventArgs e) => AddContainer(new FileInfo(e.FullPath));

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
        if (!fileInfo.AsRefreshed().Exists)
            return;

        if (fileInfo.Extension != ".cs")
            return;

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

    private void Dispose(bool disposing)
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

    private sealed class FileInfoFullNameComparer : IEqualityComparer<FileInfo>
    {
        public bool Equals(FileInfo x, FileInfo y) => EqualityComparer<string>.Default.Equals(x?.FullName, y?.FullName);
        public int GetHashCode([DisallowNull] FileInfo obj) => HashCode.Combine(obj.FullName);
    }
}
