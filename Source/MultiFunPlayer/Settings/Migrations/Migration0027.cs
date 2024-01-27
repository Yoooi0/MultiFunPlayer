using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0027 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.ContainsKey("Shortcuts"))
        {
            Logger.Info("Renamed \"Shortcuts\" to \"Shortcut\"");
            settings.RenameProperty("Shortcuts", "Shortcut");
        }

        if (settings.TryGetObject(out var shortcutSettings, "Shortcut"))
            MigrateShortcuts(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateShortcuts(JObject settings)
    {
        if (settings.ContainsKey("Bindings"))
        {
            settings.RenameProperty("Bindings", "Shortcuts");
            Logger.Info("Renamed \"Shortcut.Bindings\" to \"Shortcut.Shortcuts\"");
        }

        var gestureToShortcutMap = new Dictionary<string, string>()
        {
            ["MultiFunPlayer.Input.RawInput.KeyboardGestureDescriptor, MultiFunPlayer"] =    "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
            ["MultiFunPlayer.Input.RawInput.MouseButtonGestureDescriptor, MultiFunPlayer"] = "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
            ["MultiFunPlayer.Input.RawInput.MouseAxisGestureDescriptor, MultiFunPlayer"] =   "MultiFunPlayer.Shortcut.AxisDriveShortcut, MultiFunPlayer",
            ["MultiFunPlayer.Input.TCode.TCodeButtonGestureDescriptor, MultiFunPlayer"] =    "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
            ["MultiFunPlayer.Input.TCode.TCodeAxisGestureDescriptor, MultiFunPlayer"] =      "MultiFunPlayer.Shortcut.AxisDriveShortcut, MultiFunPlayer",
            ["MultiFunPlayer.Input.XInput.GamepadButtonGestureDescriptor, MultiFunPlayer"] = "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
            ["MultiFunPlayer.Input.XInput.GamepadAxisGestureDescriptor, MultiFunPlayer"] =   "MultiFunPlayer.Shortcut.AxisDriveShortcut, MultiFunPlayer",
        };

        foreach(var shortcut in settings["Shortcuts"].OfType<JObject>())
        {
            if (!shortcut.ContainsKey("Gesture"))
                continue;
            if (shortcut["Gesture"] is not JObject gesture || !gesture.ContainsKey("$type"))
                continue;

            var gestureType = gesture["$type"].ToString();
            var shortcutType = gestureToShortcutMap[gestureType];

            shortcut.Add("$type", shortcutType);
            Logger.Info($"Marked \"{gestureType}\" binding as \"{shortcutType}\" shortcut");
        }

        var migratedCounter = 0;
        foreach(var action in settings.SelectTokens("$.Shortcuts[*].Actions[?(@.Name =~ /Shortcut::Enabled::.*/i)]").OfType<JObject>())
        {
            if (!action.ContainsKey("Settings"))
                continue;
            if (action["Settings"].First is not JObject gestureSetting)
                continue;

            var gestureSettingJson = gestureSetting.ToString(Formatting.None);
            var matchedShortcut = FindShortcutByGestureJson(gestureSettingJson);
            if (matchedShortcut == null)
            {
                Logger.Warn($"Unable to find matching shortcut for \"{action["Name"]}\" action!");
                continue;
            }

            if (!matchedShortcut.ContainsKey("Name"))
            {
                var newName = $"migrated{migratedCounter++}";
                matchedShortcut.Add("Name", newName);
                Logger.Info($"Naming matched shortcut to \"{newName}\"");
            }

            var shortcutName = matchedShortcut["Name"];
            Logger.Info($"Matched \"{action["Name"]}\" action to \"{shortcutName}\" shortcut");

            gestureSetting["$type"] = "System.String, System.Private.CoreLib";
            gestureSetting["Value"] = shortcutName;

            foreach (var property in gestureSetting.Properties().ToList())
            {
                if (property.Name != "$type" && property.Name != "Value")
                {
                    property.Remove();
                    Logger.Info($"Removed unused property \"{property.Name}\" from action setting");
                }
            }
        }

        JObject FindShortcutByGestureJson(string findGestureJson)
        {
            foreach (var shortcut in settings["Shortcuts"].OfType<JObject>())
            {
                if (!shortcut.ContainsKey("Gesture"))
                    continue;

                var gestureJson = shortcut["Gesture"].ToString(Formatting.None);
                if (string.Equals(findGestureJson, gestureJson, StringComparison.Ordinal))
                    return shortcut;
            }

            return null;
        }
    }
}