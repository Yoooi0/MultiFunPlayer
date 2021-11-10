using Newtonsoft.Json.Linq;
using NLog;
using System.IO;

namespace MultiFunPlayer.Settings;

public enum SettingsType
{
    Application,
    Devices
}

public static class SettingsHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static string FileFormat => $"{nameof(MultiFunPlayer)}.{{0}}.json";

    public static JObject Read(SettingsType type)
    {
        var path = GetFilePath(type);
        if (!File.Exists(path))
            return null;

        Logger.Info("Reading settings from \"{0}\"", path);
        try
        {
            return JObject.Parse(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to read settings");
            return null;
        }
    }

    public static JObject ReadOrEmpty(SettingsType type) => Read(type) ?? new JObject();

    public static void Write(SettingsType type, JObject settings)
    {
        var path = GetFilePath(type);

        try
        {
            Logger.Info("Saving settings to \"{0}\"", path);
            File.WriteAllText(path, settings.ToString());
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to save settings");
        }
    }

    public static string GetFilePath(SettingsType type)
    {
        var filePostFix = type switch
        {
            SettingsType.Application => "config",
            SettingsType.Devices => "device",
            _ => throw new NotSupportedException(),
        };

        return string.Format(FileFormat, filePostFix);
    }
}
