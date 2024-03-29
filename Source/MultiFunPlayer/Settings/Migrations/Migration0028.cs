using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0028 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (TrySelectObject(settings, "$.Shortcut", out var shortcutSettings))
        {
            RemovePropertiesByName(shortcutSettings, [
                "IsKeyboardKeysGestureEnabled",
                "IsMouseAxisGestureEnabled",
                "IsMouseButtonGestureEnabled",
                "IsGamepadAxisGestureEnabled",
                "IsGamepadButtonGestureEnabled",
                "IsTCodeButtonGestureEnabled",
                "IsTCodeAxisGestureEnabled"
            ]);
        }

        base.Migrate(settings);
    }
}