using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0039 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPaths(settings, new Dictionary<string, string>
        {
            ["$.Script.Repositories.Stash.VideoMatchType"] = "LocalMatchType",
            ["$.Script.Repositories.Stash.ScriptMatchAxis"] = "DmsMatchAxis",
        }, selectMultiple: false);

        EditPropertyByPath(settings, "$.Script.Repositories.Stash.LocalMatchType", _ => "MatchToCurrentFile");
    }
}
