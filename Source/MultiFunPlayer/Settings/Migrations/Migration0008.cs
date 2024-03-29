using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0008 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        RenamePropertyByPath(settings,
            "$.LogBlacklist.['MultiFunPlayer.UI.Controls.ViewModels.ShortcutViewModel']",
            "MultiFunPlayer.UI.Controls.ViewModels.ShortcutSettingsViewModel");

        base.Migrate(settings);
    }
}