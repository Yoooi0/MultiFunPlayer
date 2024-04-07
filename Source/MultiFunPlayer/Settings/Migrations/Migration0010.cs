using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0010 : AbstractSettingsMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        var items = new string[] { "DeoVR", "HereSphere", "Internal", "MPC-HC", "MPV", "Whirligig" };
        if (TrySelectObject(settings, "$.MediaSource", out var mediaSource))
            AddPropertyByName(mediaSource, "Items", JArray.FromObject(items));
    }
}