using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0034 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPath(settings, "$.Script.AxisSettings.*.MaximumSecondsPerStroke", "SpeedLimitUnitsPerSecond");
        EditPropertiesByPath(settings, "$.Script.AxisSettings.*.SpeedLimitUnitsPerSecond", v =>
        {
            var secondsPerStroke = v.ToObject<double>();
            return secondsPerStroke == 0 ? double.PositiveInfinity : double.IsInfinity(secondsPerStroke) ? 0 : 1 / secondsPerStroke;
        });

        EditPropertiesByPath(settings, "$.Shortcut.Shortcuts[*].Actions[?(@.Name =~ /Axis::SpeedLimitSecondsPerStroke::.*/i)].Name",
            v => v.ToString().Replace("SpeedLimitSecondsPerStroke", "SpeedLimitSecondsPerUnit"));
    }
}
