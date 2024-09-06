using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0002 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPaths(settings, new Dictionary<string, string>()
        {
            ["$.LogBlacklist.['MultiFunPlayer.Common.Input.RawInput.*']"] = "MultiFunPlayer.Input.RawInput.*",
            ["$.LogBlacklist.['MultiFunPlayer.Common.Input.XInput.*']"] = "MultiFunPlayer.Input.XInput.*",
            ["$.LogBlacklist.['MultiFunPlayer.ViewModels.ShortcutViewModel']"] = "MultiFunPlayer.UI.Controls.ViewModels.ShortcutViewModel"
        }, selectMultiple: false);
    }
}