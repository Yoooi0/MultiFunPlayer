using MultiFunPlayer.Common;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__1_23_0__2 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public override int TargetVersion => 9;

    public override void Migrate(JObject settings)
    {
        if (!settings.TryGetObject(out var _, "Device"))
        {
            MigrateDevices(settings);
        }

        base.Migrate(settings);
    }

    private void MigrateDevices(JObject settings)
    {
        Logger.Info("Migrating Devices");

        var devices = DeviceSettingsViewModel.DefaultDevices.ToList();
        if (!settings.TryGetValue<string>("SelectedDevice", out var selectedDevice) || string.IsNullOrWhiteSpace(selectedDevice))
            selectedDevice = devices.Last().Name;

        var device = devices.FirstOrDefault(d => string.Equals(d.Name, selectedDevice, StringComparison.InvariantCultureIgnoreCase)) ?? devices.Last();
        var migratedName = $"{device.Name} (migrated)";
        var migratedDevice = device.Clone(migratedName);

        foreach (var axis in migratedDevice.Axes)
            axis.Enabled = true;

        Logger.Info("Created device \"{0}\"", migratedName);
        devices.Add(migratedDevice);
        selectedDevice = migratedName;

        settings["Devices"] = JArray.FromObject(devices);
        settings["SelectedDevice"] = selectedDevice;
    }
}