using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0020 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        ModifyPropertiesByPath(settings, "$.Shortcuts.Bindings[?(@.Gesture.$type =~ /.*GamepadButtonGestureDescriptor.*/i)].Gesture.Button", property =>
        {
            if (RenameProperty(ref property, "Buttons"))
                SetProperty(property, new JArray(property.Value.ToString()));
        });
    }
}