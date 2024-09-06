using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0019 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[*].Descriptor", "Name");
    }
}