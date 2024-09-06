using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0022 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        if (!TrySelectProperty(settings, "$.Script.ScriptLibraries", out var scriptLibraries))
            return;

        var localRepository = CreateChildObjects(settings, "Script", "Repositories", "Local");
        MoveProperty(scriptLibraries, localRepository, replace: true);
    }
}