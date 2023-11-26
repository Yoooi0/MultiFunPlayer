using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0020 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateGamepadButtonGestures(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateGamepadButtonGestures(JObject settings)
    {
        Logger.Info("Migrating gamepad button gestures");
        foreach (var gesture in settings.SelectTokens("$.Bindings[*].Gesture").OfType<JObject>())
        {
            gesture.Add("Buttons", new JArray(gesture["Button"].ToString()));
            gesture.Remove("Button");
            Logger.Info("Renamed gesture property from \"Button\" to \"Buttons\"");
        }
    }
}