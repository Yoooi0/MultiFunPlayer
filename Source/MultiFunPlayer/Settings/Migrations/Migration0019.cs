using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0019 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        RenamePropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[*].Descriptor", "Name");

        base.Migrate(settings);
    }
}