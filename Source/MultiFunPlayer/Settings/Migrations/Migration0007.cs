using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0007 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPaths(settings, new Dictionary<string, string>()
        {
            ["$.Script.AxisSettings.*.Inverted"] = "InvertScript",
            ["$.Script.AxisSettings.*.Scale"] = "ScriptScale"
        });

        EditPropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Axis::Inverted::.*/i)].Descriptor",
            v => Regex.Replace(v.ToString(), "^Axis::Inverted::", "Axis::InvertScript::"));
    }
}