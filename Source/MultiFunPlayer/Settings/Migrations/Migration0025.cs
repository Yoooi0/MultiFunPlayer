using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0025 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        RemovePropertyByPath(settings, "$.LogBlacklist.['MultiFunPlayer.UI.Controls.ViewModels.ShortcutSettingsViewModel']");

        base.Migrate(settings);
    }
}