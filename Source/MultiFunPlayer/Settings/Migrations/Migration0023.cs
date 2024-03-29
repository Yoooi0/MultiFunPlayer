using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0023 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        EditPropertyByPath(settings, "$.Script.Repositories.Local.Enabled", _ => true);

        base.Migrate(settings);
    }
}