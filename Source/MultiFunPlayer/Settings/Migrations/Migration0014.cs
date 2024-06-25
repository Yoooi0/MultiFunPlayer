using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0014 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        foreach (var deviceSettings in SelectObjects(settings, "$.OutputTarget.Items[?(@.$type =~ /.*ButtplugOutputTargetViewModel.*/)].DeviceSettings[*]"))
        {
            RenamePropertyByName(deviceSettings, "FeatureIndex", "ActuatorIndex");
            RenamePropertyByName(deviceSettings, "MessageType", "ActuatorType");

            EditPropertyByName(deviceSettings, "ActuatorType", v => v.ToString() switch
            {
                "VibrateCmd" => "Vibrate",
                "RotateCmd" => "Rotate",
                "LinearCmd" => "Position",
                _ => "Unknown"
            });
        }
    }
}