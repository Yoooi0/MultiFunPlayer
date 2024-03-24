using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0028 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        RemoveProperties(settings, "$.Shortcut", [
            "IsKeyboardKeysGestureEnabled",
            "IsMouseAxisGestureEnabled",
            "IsMouseButtonGestureEnabled",
            "IsGamepadAxisGestureEnabled",
            "IsGamepadButtonGestureEnabled",
            "IsTCodeButtonGestureEnabled",
            "IsTCodeAxisGestureEnabled"
        ]);

        base.Migrate(settings);
    }
}