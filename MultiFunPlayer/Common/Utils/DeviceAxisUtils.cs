using System.IO;

namespace MultiFunPlayer.Common;

public static class DeviceAxisUtils
{
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName) => FindAxesMatchingName(DeviceAxis.All, scriptName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(string scriptName, string mediaName) => FindAxesMatchingName(DeviceAxis.All, scriptName, mediaName);
    public static IEnumerable<DeviceAxis> FindAxesMatchingName(IEnumerable<DeviceAxis> axes, string scriptName)
    {
        var scriptWithoutExtension = Path.GetFileNameWithoutExtension(scriptName);
        var isUnnamedScript = !DeviceAxis.All.SelectMany(a => a.FunscriptNames)
                                             .Distinct()
                                             .Any(n => scriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase));
        return FindAxesMatchingName(axes, scriptName, isUnnamedScript);
    }

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
            if (isUnnamedScript)
                if (axis.LoadUnnamedScript)
                yield return axis;
            else if (axis.FunscriptNames.Any(n => scriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase)))
                yield return axis;
        }
    }
}
