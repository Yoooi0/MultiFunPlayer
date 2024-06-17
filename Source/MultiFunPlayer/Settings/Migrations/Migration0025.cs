using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0025 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RemovePropertyByPath(settings, "$.LogBlacklist.['MultiFunPlayer.UI.Controls.ViewModels.ShortcutSettingsViewModel']");
    }
}