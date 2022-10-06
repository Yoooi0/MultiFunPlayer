using Newtonsoft.Json.Linq;
using NLog;
using System.IO;

namespace MultiFunPlayer.Settings;

public static class SettingsHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static string Path => $"{nameof(MultiFunPlayer)}.config.json";

    public static JObject Read()
    {
        if (!File.Exists(Path))
            return null;

        Logger.Info("Reading settings from \"{0}\"", Path);
        try
        {
            return JObject.Parse(File.ReadAllText(Path));
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to read settings");
            return null;
        }
    }

    public static JObject ReadOrEmpty() => Read() ?? new JObject();

    public static void Write(JObject settings)
    {
        try
        {
            Logger.Info("Saving settings to \"{0}\"", Path);
            File.WriteAllText(Path, settings.ToString());
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to save settings");
        }
    }
}
