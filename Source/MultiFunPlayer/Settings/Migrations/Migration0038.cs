using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0038 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPaths(settings, new Dictionary<string, string>
        {
            ["$.Script.Repositories.XBVR.VideoMatchType"] = "LocalMatchType",
            ["$.Script.Repositories.XBVR.ScriptMatchType"] = "DmsMatchType",
        }, selectMultiple: false);

        EditPropertyByPath(settings, "$.Script.Repositories.XBVR.LocalMatchType", _ => "MatchToCurrentFile");
        EditPropertyByPath(settings, "$.Script.Repositories.XBVR.DmsMatchType", v => v.Value<string>() == "MatchSelectedOnly" ? v : "MatchToCurrentFile");
    }
}
