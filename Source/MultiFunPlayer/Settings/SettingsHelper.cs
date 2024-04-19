using Newtonsoft.Json.Linq;
using NLog;
using System.IO;

namespace MultiFunPlayer.Settings;

internal static class SettingsHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static JObject ReadOrEmpty(string path)
    {
        if (Read(path) is JObject settings)
            return settings;

        return [];
    }

    private static JObject Read(string path)
    {
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

    public static void Write(JObject settings, string path)
    {
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
}
