using MultiFunPlayer.UI.Controls.ViewModels;
using System.Collections.Immutable;
using System.IO;

namespace MultiFunPlayer.Common;

public static class DeviceAxisUtils
{
    private static ImmutableSortedSet<string> _knownFunscriptNames;
    public static IReadOnlySet<string> KnownFunscriptNames
    {
        get
        {
            _knownFunscriptNames ??= DeviceSettingsViewModel.DefaultDevices
                                        .SelectMany(d => d.Axes)
                                        .SelectMany(a => a.FunscriptNames)
                                        .Union(DeviceAxis.All.SelectMany(d => d.FunscriptNames))
                                        .Distinct()
                                        .ToImmutableSortedSet();

            return _knownFunscriptNames;
        }
    }

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName) => FindAxesMatchingName(DeviceAxis.All, scriptName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);

        var isUnnamedScript = !KnownFunscriptNames.Any(n => scriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase));
        return FindAxesMatchingName(axes, scriptName, isUnnamedScript);
    }

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName, string mediaName) => FindAxesMatchingName(DeviceAxis.All, scriptName, mediaName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName, string mediaName)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);
        var mediaWithoutExtension = Path.GetFileNameWithoutExtension(mediaName);
        var isUnnamedScript = string.Equals(scriptWithoutExtension, mediaWithoutExtension, StringComparison.OrdinalIgnoreCase);
        return FindAxesMatchingName(axes, scriptName, isUnnamedScript);
    }

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName, bool isUnnamedScript) => FindAxesMatchingName(DeviceAxis.All, scriptName, isUnnamedScript);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName, bool isUnnamedScript)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);
        foreach (var axis in axes)
        {
            if (isUnnamedScript && axis.LoadUnnamedScript)
                yield return axis;
            else if (!isUnnamedScript && axis.FunscriptNames.Any(n => scriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase)))
                yield return axis;
        }
    }

    public static string GetBaseNameWithExtension(string fileName)
    {
        var fileWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        var funscriptName = KnownFunscriptNames.FirstOrDefault(n => fileWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase));
        if (funscriptName == null)
            return fileName;

        var fileExtension = Path.GetExtension(fileName);
        return $"{fileWithoutExtension[..^(funscriptName.Length + 1)]}{fileExtension}";
    }
}
