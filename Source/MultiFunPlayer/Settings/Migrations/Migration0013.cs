using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0013 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPath(settings, "$.Devices[*].Default", "IsDefault");
    }
}