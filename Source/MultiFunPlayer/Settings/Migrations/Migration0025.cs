using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0025 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var logBlacklistSettings, "LogBlacklist"))
            MigrateLogBlacklist(logBlacklistSettings);

        base.Migrate(settings);
    }

    private void MigrateLogBlacklist(JObject settings)
    {
        Logger.Info("Migrating LogBlacklist");
        const string filterName = "MultiFunPlayer.UI.Controls.ViewModels.ShortcutSettingsViewModel";
        if (!settings.ContainsKey(filterName))
            return;

        settings.Remove(filterName);
        Logger.Info("Removed \"{0}\" from log blacklist", filterName);
    }
}