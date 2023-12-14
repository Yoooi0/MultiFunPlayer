using MultiFunPlayer.Common;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0009 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (!settings.TryGetValue("Devices", out var _))
            MigrateDevices(settings);

        base.Migrate(settings);
    }

    private void MigrateDevices(JObject settings)
    {
        Logger.Info("Migrating Devices");

        var devices = DeviceSettingsViewModel.DefaultDevices.ToList();
        if (!settings.TryGetValue<string>("SelectedDevice", out var selectedDevice) || string.IsNullOrWhiteSpace(selectedDevice))
            selectedDevice = devices[^1].Name;

        var device = devices.Find(d => string.Equals(d.Name, selectedDevice, StringComparison.OrdinalIgnoreCase)) ?? devices[^1];
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