using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0026 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var scriptSettings, "Script"))
            MigrateHeatmapShowStrokeLength(scriptSettings);

        base.Migrate(settings);
    }

    private void MigrateHeatmapShowStrokeLength(JObject settings)
    {
        Logger.Info("Migrating HeatmapShowStrokeLength property");
        if (!settings.ContainsKey("HeatmapShowStrokeLength"))
            return;

        settings.RenameProperty("HeatmapShowStrokeLength", "HeatmapShowRange");
        Logger.Info("Renamed property from \"HeatmapShowStrokeLength\" to \"HeatmapShowRange\"");
    }
}