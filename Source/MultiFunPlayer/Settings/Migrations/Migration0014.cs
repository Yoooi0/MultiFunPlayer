using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0014 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var outputTargetSettings, "OutputTarget"))
            MigrateButtplugOutputSettings(outputTargetSettings);

        base.Migrate(settings);
    }

    private void MigrateButtplugOutputSettings(JObject settings)
    {
        Logger.Info("Migrating ButtplugOutputTarget");

        foreach (var deviceSettings in settings.SelectTokens("$.Items[?(@.$type =~ /.*ButtplugOutputTargetViewModel.*/)].DeviceSettings[*]").OfType<JObject>())
        {
            deviceSettings.RenameProperty("FeatureIndex", "ActuatorIndex");
            Logger.Info("Migrated property from \"FeatureIndex\" to \"ActuatorIndex\"");

            var messageType = deviceSettings["MessageType"];
            var actuatorType = messageType.ToString() switch
            {
                "VibrateCmd" => "Vibrate",
                "RotateCmd" => "Rotate",
                "LinearCmd" => "Position",
                _ => "Unknown"
            };

            deviceSettings.Remove("MessageType");
            deviceSettings.Add("ActuatorType", JToken.FromObject(actuatorType));
            Logger.Info($"Migrated \"MessageType={messageType}\" to \"ActuatorType={actuatorType}\"");
        }
    }
}