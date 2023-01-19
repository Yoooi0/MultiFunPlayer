using MultiFunPlayer.UI.Controls.ViewModels;
using System.IO;

namespace MultiFunPlayer.Common;

public static class DeviceAxisUtils
{
    public static IEnumerable<string> KnownFunscriptNames { get; } = DeviceSettingsViewModel.DefaultDevices.SelectMany(d => d.Axes)
                                                                                                           .SelectMany(a => a.FunscriptNames)
                                                                                                           .Distinct()
                                                                                                           .ToList();

    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName) => FindAxesMatchingName(DeviceAxis.All, scriptName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);

        var funscriptNames = KnownFunscriptNames.Union(DeviceAxis.All.SelectMany(d => d.FunscriptNames));
        var isUnnamedScript = !funscriptNames.Any(n => scriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase));
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
}
