using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0035 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPath(settings, "$.Shortcut.Shortcuts[*].Actions[?(@.Name =~ /Axis::Range::.*/i)].Settings[?(@.$type=~ /System\\.Int32.*/i)].$type",
            _ => "System.Double, System.Private.CoreLib");

        EditPropertiesByPaths(settings, [
            "$.Shortcut.Shortcuts[*].Actions[?(@.Name =~ /Axis::Range::.*/i)].Settings[?(@.$type=~ /System\\.Double.*/i)].Value",
            "$.OutputTarget.Items[*].AxisSettings.*.Minimum",
            "$.OutputTarget.Items[*].AxisSettings.*.Maximum"],
            v => v.ToObject<double>() / 100, selectMultiple: true);
    }
}
