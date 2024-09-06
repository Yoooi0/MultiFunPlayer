using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0023 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        if (!TrySelectObject(settings, "$.Script.Repositories.Local", out var localRepository))
            return;

        SetPropertyByName(localRepository, "Enabled", true, addIfMissing: true);
    }
}