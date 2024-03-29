using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System.Diagnostics;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0027 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        RenamePropertyByName(settings, "Shortcuts", "Shortcut");

        if (settings.TryGetObject(out var shortcutSettings, "Shortcut"))
            MigrateShortcuts(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateShortcuts(JObject settings)
    {
        RenamePropertyByPath(settings, "$.Shortcut.Bindings", "Shortcuts");

        foreach (var shortcut in SelectObjects(settings, "$.Shortcut.Shortcuts"))
        {
            if (!TryGetValue<JObject>(shortcut, "Gesture", out var gesture))
                continue;
            if (!TryGetValue<JValue>(gesture, "$type", out var gestureType))
                continue;

            AddPropertyByName(shortcut, "$type", gestureType.ToString() switch
            {
                "MultiFunPlayer.Input.RawInput.KeyboardGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
                "MultiFunPlayer.Input.RawInput.MouseButtonGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
                "MultiFunPlayer.Input.RawInput.MouseAxisGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.AxisDriveShortcut, MultiFunPlayer",
                "MultiFunPlayer.Input.TCode.TCodeButtonGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
                "MultiFunPlayer.Input.TCode.TCodeAxisGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.AxisDriveShortcut, MultiFunPlayer",
                "MultiFunPlayer.Input.XInput.GamepadButtonGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.ButtonReleaseShortcut, MultiFunPlayer",
                "MultiFunPlayer.Input.XInput.GamepadAxisGestureDescriptor, MultiFunPlayer" => "MultiFunPlayer.Shortcut.AxisDriveShortcut, MultiFunPlayer",
                _ => throw new UnreachableException()
            });
        }

        var migratedCounter = 0;
        foreach (var action in SelectObjects(settings, "$.Shortcuts[*].Actions[?(@.Name =~ /Shortcut::Enabled::.*/i)]"))
        {
            if (!TrySelectObject(action, "$.Settings[0]", out var gesture))
                continue;

            var matchedShortcut = FindShortcutByGestureJson(gesture.ToString(Formatting.None));
            if (matchedShortcut == null)
            {
                //TODO: remove??
                Logger.Warn($"Unable to find matching shortcut for \"{action["Name"]}\" action");
                continue;
            }

            if (!matchedShortcut.ContainsKey("Name"))
                AddPropertyByName(matchedShortcut, "Name", $"migrated{migratedCounter++}");

            RemoveAllProperties(gesture);
            AddPropertiesByName(gesture, new Dictionary<string, JToken>()
            {
                ["$type"] = "System.String, System.Private.CoreLib",
                ["Value"] = matchedShortcut["Name"].ToString()
            });
        }

        JObject FindShortcutByGestureJson(string findGestureJson)
        {
            foreach (var shortcut in SelectObjects(settings, "$.Shortcuts[*]"))
            {
                if (TryGetValue<JObject>(shortcut, "Gesture", out var gesture))
                    continue;

                var gestureJson = gesture.ToString(Formatting.None);
                if (string.Equals(findGestureJson, gestureJson, StringComparison.Ordinal))
                    return shortcut;
            }

            return null;
        }
    }
}