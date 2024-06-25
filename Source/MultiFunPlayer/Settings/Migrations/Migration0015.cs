using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0015 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        foreach (var axisSettings in SelectObjects(settings, "$.Script.AxisSettings.*"))
        {
            if (!TryGetProperty(axisSettings, "Bypass", out var bypass))
                continue;

            AddPropertiesByName(axisSettings, new Dictionary<string, JToken>()
            {
                ["BypassScript"] = bypass.Value,
                ["BypassMotionProvider"] = bypass.Value,
                ["BypassTransition"] = bypass.Value
            });

            RemoveProperty(bypass);
        }

        foreach (var action in SelectObjects(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Axis::Bypass::.*/i)]"))
        {
            EditPropertyByName(action, "Descriptor",
                v => Regex.Replace(v.ToString(), "^Axis::Bypass::", "Axis::Bypass::All::"));
        }
    }
}