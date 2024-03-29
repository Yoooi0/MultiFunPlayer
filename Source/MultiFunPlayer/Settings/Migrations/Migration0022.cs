using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0022 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (TrySelectProperty(settings, "$.Script.ScriptLibraries", out var scriptLibraries))
        {
            var localRepository = CreateChildObjects(settings, "Script", "Repositories", "Local");
            MoveProperty(scriptLibraries, localRepository, replace: true);
        }

        base.Migrate(settings);
    }
}