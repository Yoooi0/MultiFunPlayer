using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0022 : AbstractSettingsMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        if (!TrySelectProperty(settings, "$.Script.ScriptLibraries", out var scriptLibraries))
            return;

        var localRepository = CreateChildObjects(settings, "Script", "Repositories", "Local");
        MoveProperty(scriptLibraries, localRepository, replace: true);
    }
}