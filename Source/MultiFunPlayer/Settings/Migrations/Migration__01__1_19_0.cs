using Microsoft.Win32;
using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;
using System.Management;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal class Migration__01__1_19_0 : AbstractConfigMigration
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var serialSettings, "OutputTarget", "Serial"))
            MigrateSerialOutputTargetSelectedComPort(serialSettings);

        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateSerialComPortSetAction(shortcutSettings);

        base.Migrate(settings);
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

        try { return serialPort?.GetPropertyValue("DeviceID") as string; } catch { }

        return null;
    }

    private void MigrateSerialOutputTargetSelectedComPort(JObject settings)
    {
        Logger.Info("Migrating SerialOutputTarget SelectedComPort");
        if (settings.TryGetValue<string>("SelectedComPort", out var selectedComPort))
        {
            var deviceId = GetComPortDeviceId(selectedComPort);
            if (deviceId != null)
            {
                settings["SelectedSerialPort"] = deviceId;
                Logger.Info("Migrated SelectedComPort from \"{0}\" to \"{1}\"", selectedComPort, settings["SelectedSerialPort"]);
            }
            else
            {
                Logger.Warn("Could not find DeviceID for \"{0}\"", selectedComPort);
            }
        }

        settings.Remove("SelectedComPort");
        Logger.Info("Removed \"SelectedComPort\"");
    }

    private void MigrateSerialComPortSetAction(JObject settings)
    {
        if (!settings.TryGetValue("Bindings", out var bindingsToken))
            return;

        Logger.Info("Migrating \"Serial::ComPort::Set\" action");
        foreach (var binding in bindingsToken.OfType<JObject>())
        {
            if (!binding.TryGetValue("Actions", out var actionsToken))
                continue;

            foreach (var action in actionsToken.OfType<JObject>())
            {
                if (!action.ContainsKey("Descriptor") || !string.Equals(action["Descriptor"].ToString(), "Serial::ComPort::Set"))
                    continue;

                if (!action.TryGetValue("Settings", out var settingsToken))
                    continue;

                if (settingsToken.FirstOrDefault() is not JObject settingToken)
                    continue;

                if (!settingToken.ContainsKey("Value"))
                    continue;

                var comPort = settingToken["Value"].ToString();
                var deviceId = GetComPortDeviceId(comPort);
                if (deviceId != null)
                {
                    action["Descriptor"] = "Serial::SerialPort::Set";
                    settingToken["Value"] = deviceId;

                    Logger.Info("Migrated \"Serial::ComPort::Set [{0}]\" to \"Serial::SerialPort::Set [{1}]\"", comPort, deviceId);
                }
                else
                {
                    Logger.Warn("Could not find DeviceID for \"{0}\"", comPort);
                }
            }
        }
    }
}
