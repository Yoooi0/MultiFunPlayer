using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0014 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
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

        base.Migrate(settings);
    }
}