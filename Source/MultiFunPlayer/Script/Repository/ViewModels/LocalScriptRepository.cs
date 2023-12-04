using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;

namespace MultiFunPlayer.Script.Repository.ViewModels;

[DisplayName("Local")]
[JsonObject(MemberSerialization.OptIn)]
internal sealed class LocalScriptRepository(IEventAggregator eventAggregator) : AbstractScriptRepository, ILocalScriptRepository
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [JsonProperty] public ObservableConcurrentCollection<ScriptLibrary> ScriptLibraries { get; } = [];

    public override ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
        => ValueTask.FromResult(SearchForScripts(mediaResource.Name, mediaResource.Source, axes));

    public Dictionary<DeviceAxis, IScriptResource> SearchForScripts(string mediaName, string mediaSource, IEnumerable<DeviceAxis> axes)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return [];

        Logger.Debug("Maching files to axes [Axes: {list}]", axes);

        var searchResult = new Dictionary<DeviceAxis, IScriptResource>();
        var mediaWithoutExtension = Path.GetFileNameWithoutExtension(mediaName);
        if (ScriptLibraries != null)
        {
            foreach (var library in ScriptLibraries)
            {
                Logger.Info("Searching library \"{0}\"", library.Directory);
                foreach (var zipFile in library.EnumerateFiles($"{mediaWithoutExtension}.zip"))
                    TryMatchArchive(zipFile.FullName);

                foreach (var funscriptFile in library.EnumerateFiles($"{mediaWithoutExtension}.*funscript"))
                    TryMatchName(funscriptFile.Name, FunscriptReader.Default.FromFileInfo(funscriptFile));
            }
        }

        if (Directory.Exists(mediaSource))
        {
            Logger.Info("Searching media location \"{0}\"", mediaSource);
            var sourceDirectory = new DirectoryInfo(mediaSource);
            TryMatchArchive(Path.Join(sourceDirectory.FullName, $"{mediaWithoutExtension}.zip"));

            foreach (var funscriptFile in sourceDirectory.EnumerateFiles($"{mediaWithoutExtension}.*funscript"))
                TryMatchName(funscriptFile.Name, FunscriptReader.Default.FromFileInfo(funscriptFile));
        }

        return searchResult;

        void SaveScriptToResult(DeviceAxis axis, IScriptResource resource)
        {
            Logger.Debug("Matched {0} script to \"{1}\"", axis, resource.Name);
            if (searchResult.TryGetValue(axis, out var existingResource))
                Logger.Warn("Overwriting {0} script from \"{1}\"", axis, existingResource.Name);

            searchResult[axis] = resource;
        }

        void TryMatchName(string scriptName, ScriptReaderResult readerResult)
        {
            if (!readerResult.IsSuccess)
                return;

            if (readerResult.IsMultiAxis)
            {
                foreach (var (axis, resource) in readerResult.Resources)
                    SaveScriptToResult(axis, resource);
            }
            else
            {
                foreach (var axis in DeviceAxisUtils.FindAxesMatchingName(axes, scriptName, mediaName))
                    SaveScriptToResult(axis, readerResult.Resource);
            }
        }

        void TryMatchArchive(string archivePath)
        {
            if (!File.Exists(archivePath))
                return;

            Logger.Info("Matching zip file \"{0}\"", archivePath);
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries.Where(e => string.Equals(Path.GetExtension(e.FullName), ".funscript", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open();
                TryMatchName(entry.Name, FunscriptReader.Default.FromStream(entry.Name, archivePath, stream));
            }
        }
    }

    public void OnLibraryAdd(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true)
            return;

        var directory = new DirectoryInfo(dialog.FolderName);
        ScriptLibraries.Add(new ScriptLibrary(directory));

        eventAggregator.Publish(new ReloadScriptsRequestMessage());
    }

    public void OnLibraryDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
            return;

        ScriptLibraries.Remove(library);

        eventAggregator.Publish(new ReloadScriptsRequestMessage());
    }

    public void OnLibraryOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
            return;

        Process.Start("explorer.exe", library.Directory.FullName);
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
        {
            base.HandleSettings(settings, action);
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<List<ScriptLibrary>>(nameof(ScriptLibraries), out var scriptLibraries))
                ScriptLibraries.SetFrom(scriptLibraries);
        }
    }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ScriptLibrary(DirectoryInfo directory) : PropertyChangedBase
{
    [JsonProperty] public DirectoryInfo Directory { get; } = directory;
    [JsonProperty] public bool Recursive { get; set; }

    public IEnumerable<FileInfo> EnumerateFiles(string searchPattern) => Directory.SafeEnumerateFiles(searchPattern, IOUtils.CreateEnumerationOptions(Recursive));
}
