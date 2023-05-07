using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal class Migration0013 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetValue("Devices", out var devicesToken) && devicesToken is JArray deviceSettings)
            MigrateDeviceDefaultProperty(deviceSettings);

        base.Migrate(settings);
    }

    private void MigrateDeviceDefaultProperty(JArray deviceSettings)
    {
        Logger.Info("Migrating devices");

        foreach (var device in deviceSettings.OfType<JObject>())
        {
            device.RenameProperty("Default", "IsDefault");
            Logger.Info("Migrated property from \"Default\" to \"IsDefault\"");
        }
    }
}