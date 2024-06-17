using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0018 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPath(settings, "$.Script.AxisSettings.*.InvertScript", "InvertValue");

        EditPropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Axis::InvertScript::.*/i)].Descriptor",
            v => Regex.Replace(v.ToString(), "^Axis::InvertScript::", "Axis::InvertValue::"));
    }
}