using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0036 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPaths(settings, [
            CreateActionPath("^Axis::ScriptScale::(Offset|Set)"),
            CreateActionPath("^Axis::MotionProviderBlend::(Offset|Set)"),
            CreateActionPath("^Media::Speed::(Offset|Set)"),
            CreateActionPath("^Media::Position::Percent::(Offset|Set)"),
            CreateActionPath("^MotionProvider::.+?::(Speed|Minimum|Maximum)::(Offset|Set)"),
            "$.MotionProvider.*.[*].Minimum",
            "$.MotionProvider.*.[*].Maximum",
            "$.Script.AxisSettings.*.MotionProviderBlend",
            "$.Script.AxisSettings.*.ScriptScale"],
            v => v.ToObject<double>() / 100, selectMultiple: true);

        static string CreateActionPath(string actionRegex)
            => $"$.Shortcut.Shortcuts[*].Actions[?(@.Name =~ /{actionRegex}/i)].Settings[?(@.$type=~ /System\\.Double.*/i)].Value";
    }
}
