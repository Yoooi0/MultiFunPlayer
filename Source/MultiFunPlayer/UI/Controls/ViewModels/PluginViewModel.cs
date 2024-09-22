using MultiFunPlayer.Common;
using MultiFunPlayer.Plugin;
using MultiFunPlayer.Shortcut;
using Stylet;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class PluginViewModel : Screen, IHandle<SettingsMessage>, IDisposable
{
    private readonly IShortcutManager _shortcutManager;
    private FileSystemWatcher _watcher;

    public ObservableConcurrentDictionary<FileInfo, PluginContainer> Containers { get; }

    public PluginViewModel(IEventAggregator eventAggregator,  IShortcutManager shortcutManager)
    {
        eventAggregator.Subscribe(this);

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

        container.UnregisterActions(_shortcutManager);
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

        container.RegisterActions(_shortcutManager);
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Loading)
            return;

        foreach (var (_, container) in Containers)
            container.HandleSettings(message.Action);
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
