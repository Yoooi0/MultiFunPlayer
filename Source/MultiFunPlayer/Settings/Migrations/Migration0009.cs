using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0009 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        var defaultDevices = DeviceSettings.DefaultDevices.ToList();

        AddPropertyByName(settings, "Devices", JArray.FromObject(defaultDevices), out var devices);
        if (!TryGetValue<JToken>(settings, "SelectedDevice", out var selectedDevice) || string.IsNullOrWhiteSpace(selectedDevice.ToObject<string>()))
            selectedDevice = defaultDevices[^1].Name;

        SetPropertyByName(settings, "SelectedDevice", selectedDevice, addIfMissing: true);

        var device = defaultDevices.Find(d => string.Equals(d.Name, selectedDevice.ToObject<string>(), StringComparison.OrdinalIgnoreCase)) ?? defaultDevices[^1];
        var migratedName = $"{device.Name} (migrated)";
        var migratedDevice = device.Clone(migratedName);

        foreach (var axis in migratedDevice.Axes)
            axis.Enabled = true;

        AddTokenToContainer(JObject.FromObject(migratedDevice), devices.Value as JArray);

        base.Migrate(settings);
    }
}
