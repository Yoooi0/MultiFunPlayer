using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0009 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        var devices = DeviceSettings.DefaultDevices.ToList();
        if (!TryGetValue<JToken>(settings, "SelectedDevice", out var selectedDevice) || string.IsNullOrWhiteSpace(selectedDevice.ToObject<string>()))
            selectedDevice = devices[^1].Name;

        SetPropertyByName(settings, "SelectedDevice", selectedDevice, addIfMissing: true);

        var device = devices.Find(d => string.Equals(d.Name, selectedDevice.ToObject<string>(), StringComparison.OrdinalIgnoreCase)) ?? devices[^1];
        var migratedName = $"{device.Name} (migrated)";
        var migratedDevice = device.Clone(migratedName);

        foreach (var axis in migratedDevice.Axes)
            axis.Enabled = true;

        devices.Add(migratedDevice);
        SetPropertyByName(settings, "Devices", JArray.FromObject(devices), addIfMissing: true);

        base.Migrate(settings);
    }
}
