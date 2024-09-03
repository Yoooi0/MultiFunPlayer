using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0010 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        var items = new string[] { "DeoVR", "HereSphere", "Internal", "MPC-HC", "MPV", "Whirligig" };
        if (TrySelectObject(settings, "$.MediaSource", out var mediaSource))
            AddPropertyByName(mediaSource, "Items", JArray.FromObject(items));
    }
}