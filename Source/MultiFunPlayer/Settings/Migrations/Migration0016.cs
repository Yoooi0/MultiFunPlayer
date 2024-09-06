using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0016 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        foreach (var binding in SelectObjects(settings, "$.Shortcuts.Bindings[*]"))
            AddPropertyByName(binding, "Enabled", true);
    }
}