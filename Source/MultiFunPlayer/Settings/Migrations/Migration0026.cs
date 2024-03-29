using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0026 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        RenamePropertyByPath(settings, "$.Script.HeatmapShowStrokeLength", "HeatmapShowRange");

        base.Migrate(settings);
    }
}