using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0012 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        SetPropertiesByPath(settings,
            "$.Shortcuts.Bindings[*].Actions[*].Settings[?(@.$type == 'System.Single, System.Private.CoreLib')].$type",
            "System.Double, System.Private.CoreLib");

        base.Migrate(settings);
    }
}