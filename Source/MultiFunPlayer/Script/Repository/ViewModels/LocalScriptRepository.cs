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
    [JsonProperty] public new bool Enabled { get; set; } = true;

    public override ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
        => ValueTask.FromResult(SearchForScripts(mediaResource.Name, mediaResource.Source, axes));

    public Dictionary<DeviceAxis, IScriptResource> SearchForScripts(string mediaName, string mediaSource, IEnumerable<DeviceAxis> axes)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return [];

        var mediaWithoutExtension = Path.GetFileNameWithoutExtension(mediaName);
        var foundScripts = new List<ScriptReaderResult>();
        if (Directory.Exists(mediaSource))
        {
            var sourceDirectory = new DirectoryInfo(mediaSource);
            Logger.Debug("Searching media location \"{0}\"", sourceDirectory.FullName);

            AddToFound(foundScripts, EnumerateArchive(Path.Join(sourceDirectory.FullName, $"{mediaWithoutExtension}.zip")));
            AddToFound(foundScripts, sourceDirectory.EnumerateFiles($"{mediaWithoutExtension}.*funscript")
                                                    .OrderBy(i => i.FullName)
                                                    .Select(FunscriptReader.Default.FromFileInfo));
        }

        foreach (var library in ScriptLibraries)
        {
            Logger.Debug("Searching library \"{0}\"", library.Directory);
            AddToFound(foundScripts, library.EnumerateFiles($"{mediaWithoutExtension}.zip")
                                            .SelectMany(i => EnumerateArchive(i.FullName)));
            AddToFound(foundScripts, library.EnumerateFiles($"{mediaWithoutExtension}.*funscript")
                                            .Select(FunscriptReader.Default.FromFileInfo));
        }

        Logger.Debug("Found {0} scripts matching \"{1}\"", foundScripts.Count, mediaName);
        if (foundScripts.Count == 0)
            return [];

        Logger.Debug("Maching scripts to axes {list}", axes);

        var searchResult = new Dictionary<DeviceAxis, IScriptResource>();

        var multiAxisLookup = foundScripts.ToLookup(r => r.IsMultiAxis);
        foreach (var multiAxisScript in multiAxisLookup[true])
        {
            if (searchResult.Count == 0)
            {
                Logger.Debug("Matching multi-axis script [Name: \"{0}\", Source: \"{1}\"]", multiAxisScript.Name, multiAxisScript.Source);
                foreach (var axis in axes)
                    AddToResult(searchResult, axis, multiAxisScript.Resources.GetValueOrDefault(axis, null));
            }
            else
            {
                Logger.Debug("Ignoring multi-axis script [Name: \"{0}\", Source: \"{1}\"] because one was already matched", multiAxisScript.Name, multiAxisScript.Source);
            }
        }

        var singleAxisScriptNames = multiAxisLookup[false].Select(r => r.Name).Distinct().ToList();
        var singleAxisScriptLookup = multiAxisLookup[false].ToLookup(r => r.Name);
        foreach (var axis in axes)
            foreach (var matchedScriptName in DeviceAxisUtils.FindNamesMatchingAxis(axis, singleAxisScriptNames, mediaName))
                foreach (var matchedScript in singleAxisScriptLookup[matchedScriptName])
                    AddToResult(searchResult, axis, matchedScript.Resource);

        return searchResult;

        static void AddToResult(IDictionary<DeviceAxis, IScriptResource> result, DeviceAxis axis, IScriptResource resource)
        {
            if (result.TryAdd(axis, resource))
                Logger.Debug("Matched {0} script to [Name: \"{1}\", Source: \"{2}\"]", axis, resource?.Name, resource?.Source);
            else
                Logger.Debug("Ignoring {0} script [Name: \"{1}\", Source: \"{2}\"] because {0} is already matched to a script", axis, resource?.Name, resource?.Source);
        }

        static void AddToFound(IList<ScriptReaderResult> results, IEnumerable<ScriptReaderResult> newResults)
        {
            foreach (var newResult in newResults)
            {
                Logger.Debug("Found script [Name: \"{0}\", Source: \"{1}\", IsMultiAxis: {2}]", newResult.Name, newResult.Source, newResult.IsMultiAxis);
                results.Add(newResult);
            }
        }

        static IEnumerable<ScriptReaderResult> EnumerateArchive(string archivePath)
        {
            if (!File.Exists(archivePath))
                yield break;

            Logger.Debug("Searching zip file \"{0}\"", archivePath);
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries.Where(e => string.Equals(Path.GetExtension(e.FullName), ".funscript", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open();
                yield return FunscriptReader.Default.FromStream(entry.Name, archivePath, stream);
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
            if (settings.TryGetValue<bool>(nameof(Enabled), out var enabled))
                Enabled = enabled;
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

    public IEnumerable<FileInfo> EnumerateFiles(string searchPattern) => Directory.SafeEnumerateFiles(searchPattern, IOUtils.CreateEnumerationOptions(Recursive)).OrderBy(i => i.FullName);
}
