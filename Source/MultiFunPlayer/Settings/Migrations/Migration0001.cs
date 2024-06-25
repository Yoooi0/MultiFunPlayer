using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NLog;
using System.Management;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0001 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        if (TrySelectObject(settings, "$.OutputTarget.Serial", out var serial)
         && TrySelectProperty(serial, "SelectedComPort", out var selectedComPort))
        {
            var deviceId = GetComPortDeviceId(selectedComPort.Value.ToObject<string>());
            AddPropertyByName(serial, "SelectedSerialPort", deviceId);
            RemoveProperty(selectedComPort);
        }

        foreach (var action in SelectObjects(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Serial::ComPort::Set.*/i)]"))
        {
            EditPropertiesByPaths(action, new Dictionary<string, Func<JToken, JToken>>()
            {
                ["$.Descriptor"] = _ => "Serial::SerialPort::Set",
                ["$.Settings[0].Value"] = v => GetComPortDeviceId(v.ToObject<string>())
            });
        }
    }

    private string GetComPortDeviceId(string comPort)
    {
        using var entity = new ManagementClass("Win32_PnPEntity");
        var serialPort = entity.GetInstances().OfType<ManagementObject>().FirstOrDefault(o =>
        {
            try
            {
                var name = o.GetPropertyValue("Name") as string;
                if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"\(COM\d+\)"))
                    return false;

                var deviceId = o.GetPropertyValue("DeviceID") as string;
                var deviceComPort = Registry.GetValue($@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Enum\{deviceId}\Device Parameters", "PortName", "").ToString();

                return string.Equals(deviceComPort, comPort, StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            return false;
        });

        var result = default(string);
        try {
            result = serialPort?.GetPropertyValue("DeviceID") as string;
        } catch { }

        if (result == null)
            Logger.Warn("Could not find DeviceID for \"{0}\"", comPort);

        return result;
    }
}
