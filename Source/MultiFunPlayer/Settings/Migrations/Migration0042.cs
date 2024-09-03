using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0042 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPath(settings, "$.Shortcut.Shortcuts[?(@.$type=~ /MultiFunPlayer.Shortcut.ButtonLongPressShortcut.*/i)].$type",
            v => v.ToString().Replace("ButtonLongPressShortcut", "ButtonHoldShortcut"));
    }
}
