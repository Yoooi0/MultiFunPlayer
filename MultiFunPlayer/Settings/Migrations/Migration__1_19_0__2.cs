using MultiFunPlayer.Common;
using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.XInput;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__1_19_0__2 : AbstractConfigMigration
{
    private Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public override int TargetVersion => 2;

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var logBlacklistSettings, "LogBlacklist"))
        {
            MigrateLogBlacklist(logBlacklistSettings);
        }

        base.Migrate(settings);
    }

    private void MigrateLogBlacklist(JObject settings)
    {
        Logger.Info("Migrating LogBlacklist");
        var filterMap = new Dictionary<string, string>()
        {
            ["MultiFunPlayer.Common.Input.RawInput.*"] = $"{typeof(RawInputProcessor).Namespace}.*",
            ["MultiFunPlayer.Common.Input.XInput.*"] = $"{typeof(XInputProcessor).Namespace}.*",
            ["MultiFunPlayer.ViewModels.ShortcutViewModel"] = $"{typeof(ShortcutViewModel).FullName}"
        };

        foreach (var (from, to) in filterMap)
        {
            if (!settings.ContainsKey(from))
                continue;

            var value = settings[from];
            settings.Remove(from);
            settings.Add(to, value);

            Logger.Info("Migrated from \"{0}\" to \"{1}\"", from, to);
        }
    }
}