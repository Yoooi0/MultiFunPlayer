using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;
using System.IO;

namespace MultiFunPlayer.Settings;

internal static class SettingsHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string DefaultPath = $"{nameof(MultiFunPlayer)}.config.json";

    private static List<IConfigMigration> Migrations { get; set; }

    public static JObject ReadOrEmpty(string path = DefaultPath)
    {
        if (Read(path) is JObject settings)
            return settings;

        return new JObject()
        {
            ["ConfigVersion"] = Migrations.Select(m => m.TargetVersion).DefaultIfEmpty(1).Max()
        };
    }
    public static JObject Read(string path = DefaultPath)
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

    public static void Write(JObject settings, string path = DefaultPath)
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

    public static bool Migrate(JObject settings)
    {
        var dirty = false;

        var settingsVersion = settings.TryGetValue<int>("ConfigVersion", out var version) ? version : -1;
        var pendingMigrations = Migrations.Where(m => m.TargetVersion > settingsVersion)
                                          .OrderBy(m => m.TargetVersion);

        foreach (var migration in pendingMigrations)
        {
            Logger.Info("Migrating settings to version {0}", migration.TargetVersion);
            migration.Migrate(settings);
            dirty = true;
        }

        return dirty;
    }

    internal static void Initialize(IEnumerable<IConfigMigration> migrations)
    {
        Migrations = migrations.ToList();
    }
}
