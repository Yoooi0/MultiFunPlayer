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
        RenamePropertyByPath(settings, "$.Shortcut.Bindings", "Shortcuts");

        foreach (var shortcut in SelectObjects(settings, "$.Shortcut.Shortcuts[*]"))
        {
            var gestureType = SelectValue(shortcut, "$.Gesture.$type");
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
        foreach (var action in SelectObjects(settings, "$.Shortcut.Shortcuts[*].Actions[?(@.Name =~ /Shortcut::Enabled::.*/i)]"))
        {
            var gesture = SelectObject(action, "$.Settings[0]");
            var matchedShortcut = FindShortcutByGestureJson(gesture.ToString(Formatting.None));
            if (matchedShortcut == null)
            {
                Logger.Warn($"Unable to find matching shortcut for \"{action["Name"]}\" action");
                RemoveToken(action);
                continue;
            }

            if (!matchedShortcut.ContainsKey("Name"))
                AddPropertyByName(matchedShortcut, "Name", $"migrated{migratedCounter++}");

            SetPropertiesByName(gesture, new Dictionary<string, JToken>()
            {
                ["$type"] = "System.String, System.Private.CoreLib",
                ["Value"] = matchedShortcut["Name"].ToString()
            }, addIfMissing: true);
        }

        JObject FindShortcutByGestureJson(string findGestureJson)
        {
            foreach (var shortcut in SelectObjects(settings, "$.Shortcut.Shortcuts[*]"))
            {
                if (!TryGetValue<JObject>(shortcut, "Gesture", out var gesture))
                    continue;

                var gestureJson = gesture.ToString(Formatting.None);
                if (string.Equals(findGestureJson, gestureJson, StringComparison.Ordinal))
                    return shortcut;
            }

            return null;
        }

        base.Migrate(settings);
    }
}