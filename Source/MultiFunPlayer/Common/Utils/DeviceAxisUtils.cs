using System.Collections.Immutable;
using System.IO;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace MultiFunPlayer.Common;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public static class DeviceAxisUtils
{
    private static ImmutableSortedSet<string> _funscriptExtensions;
    private static IReadOnlySet<string> FunscriptExtensions
    {
        get
        {
            _funscriptExtensions ??= DeviceSettings.DefaultDevices
                                        .SelectMany(d => d.Axes)
                                        .SelectMany(a => a.FunscriptNames)
                                        .Union(DeviceAxis.All.SelectMany(d => d.FunscriptNames))
                                        .Where(n => n != "*")
                                        .Select(n => $".{n}")
                                        .Distinct()
                                        .ToImmutableSortedSet();

            return _funscriptExtensions;
        }
    }

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName) => FindAxesMatchingName(DeviceAxis.All, scriptName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);

        var isUnnamedScript = !FunscriptExtensions.Any(e => scriptWithoutExtension.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        return FindAxesMatchingName(axes, scriptName, isUnnamedScript);
    }

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName, string mediaName) => FindAxesMatchingName(DeviceAxis.All, scriptName, mediaName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName, string mediaName)
    {
        var scriptBaseName = GetBaseNameWithExtension(scriptName);
        if (!IsUnnamedScript(scriptBaseName, mediaName))
            return [];

        return FindAxesMatchingName(axes, scriptName, IsUnnamedScript(scriptName, mediaName));
    }

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName, bool isUnnamedScript) => FindAxesMatchingName(DeviceAxis.All, scriptName, isUnnamedScript);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName, bool isUnnamedScript)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);
        foreach (var axis in axes)
        {
            if (isUnnamedScript && axis.FunscriptNames.Any(n => n == "*"))
                yield return axis;
            else if (!isUnnamedScript && axis.FunscriptNames.Any(n => scriptWithoutExtension.EndsWith($".{n}", StringComparison.OrdinalIgnoreCase)))
                yield return axis;
        }
    }

    public static IEnumerable<string> FindNamesMatchingAxis(DeviceAxis axis, IEnumerable<string> scriptNames, string mediaName)
    {
        foreach (var funscriptName in axis.FunscriptNames)
        {
            foreach (var scriptName in scriptNames)
            {
                var scriptBaseName = GetBaseNameWithExtension(scriptName);
                if (!IsUnnamedScript(scriptBaseName, mediaName))
                    continue;

                var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);
                if (funscriptName == "*" && IsUnnamedScript(scriptName, mediaName))
                    yield return scriptName;
                else if (scriptWithoutExtension.EndsWith($".{funscriptName}", StringComparison.OrdinalIgnoreCase))
                    yield return scriptName;
            }
        }
    }

    public static IEnumerable<T> FindNamesMatchingAxis<T>(DeviceAxis axis, IEnumerable<T> items, Func<T, string> nameSelector, string mediaName)
    {
        var nameLookup = items.ToDictionary(i => nameSelector(i), i => i);
        var matchingNames = FindNamesMatchingAxis(axis, nameLookup.Keys, mediaName);
        return matchingNames.Select(n => nameLookup[n]);
    }

    public static string GetBaseNameWithExtension(string fileName)
    {
        var fileWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        var funscriptExtension = FunscriptExtensions.FirstOrDefault(n => fileWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase));
        if (funscriptExtension == null)
            return fileName;

        var fileExtension = Path.GetExtension(fileName);
        return $"{fileWithoutExtension[..^funscriptExtension.Length]}{fileExtension}";
    }

    private static bool IsUnnamedScript(string scriptName, string mediaName)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);
        var mediaWithoutExtension = Path.GetFileNameWithoutExtension(mediaName);
        return string.Equals(scriptWithoutExtension, mediaWithoutExtension, StringComparison.OrdinalIgnoreCase);
    }
}
