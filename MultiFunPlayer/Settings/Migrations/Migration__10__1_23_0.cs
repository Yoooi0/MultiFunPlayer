using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__10__1_23_0 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var mediaSourceSettings, "MediaSource"))
            MigrateMediaSourceItems(mediaSourceSettings);

        base.Migrate(settings);
    }

    private void MigrateMediaSourceItems(JObject settings)
    {
        Logger.Info("Migrating MediaSource items");

        if (!settings.ContainsKey("Items"))
        {
            settings["Items"] = JArray.FromObject(new[] { "DeoVR", "HereSphere", "Internal", "MPC-HC", "MPV", "Whirligig" });
            Logger.Info("Enabled all MediaSource items");
        }
    }
}