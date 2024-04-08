using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings;

internal interface ISettingsPreprocessor
{
    bool Preprocess(JObject settings);
}

internal sealed class SettingsMigrationPreprocessor(IEnumerable<ISettingsMigration> migrations) : JsonEditor, ISettingsPreprocessor
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    protected override void Log(LogLevel level, string message, params object[] args)
        => Logger.Log(LogLevel.Trace, message, args);

    public bool Preprocess(JObject settings)
    {
        if (!TryGetProperty(settings, "ConfigVersion", out var configVersion))
        {
            AddPropertyByName(settings, "ConfigVersion", migrations.Select(m => m.TargetVersion).DefaultIfEmpty(-1).Max());
            return true;
        }

        var dirty = false;
        var settingsVersion = configVersion.Value.ToObject<int>();
        var pendingMigrations = migrations.Where(m => m.TargetVersion > settingsVersion)
                                          .OrderBy(m => m.TargetVersion);

        foreach (var migration in pendingMigrations)
        {
            Logger.Info("Migrating settings to version {0}", migration.TargetVersion);
            migration.Migrate(settings);
            dirty = true;
        }

        return dirty;
    }
}

internal sealed class SettingsDevicePreprocessor : JsonEditor, ISettingsPreprocessor
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    protected override void Log(LogLevel level, string message, params object[] args)
        => Logger.Log(LogLevel.Trace, message, args);

    public bool Preprocess(JObject settings)
    {
        var dirty = false;
        var defaultDevices = JArray.FromObject(DeviceSettings.DefaultDevices);

        if (!TryGetValue<JArray>(settings, "Devices", out var devices))
        {
            settings["Devices"] = defaultDevices;
            devices = defaultDevices;
        }
        else
        {
            Logger.Trace("Updating default devices");
            foreach (var loadedDefaultDevice in SelectObjects(devices, "$.[?(@.IsDefault == true)]"))
            {
                var loadedDeviceName = loadedDefaultDevice["Name"].ToString();
                foreach (var loadedAxisSettings in SelectObjects(loadedDefaultDevice, "$.Axes[*]"))
                {
                    var loadedAxisName = loadedAxisSettings["Name"];
                    var loadedAxisEnabled = loadedAxisSettings["Enabled"].ToObject<bool>();
                    if (TrySelectProperty(defaultDevices, $"$.[?(@.Name == /^{Regex.Escape(loadedDeviceName)}$/i)].Axes[?(@.Name == '{loadedAxisName}')].Enabled", out var defaultAxisEnabled)
                     && defaultAxisEnabled.Value.ToObject<bool>() != loadedAxisEnabled)
                        SetProperty(defaultAxisEnabled, loadedAxisEnabled);
                }

                loadedDefaultDevice.Remove();
            }

            var insertIndex = 0;
            foreach (var defaultDevice in defaultDevices)
                InsertTokenToArray(defaultDevice, insertIndex++, devices);
        }

        if (!TryGetProperty(settings, "SelectedDevice", out var selectedDevice))
        {
            AddPropertyByName(settings, "SelectedDevice", devices.Last["Name"].ToString(), out selectedDevice);
            dirty = true;
        }
        else if (string.IsNullOrWhiteSpace(selectedDevice.ToString()))
        {
            SetProperty(selectedDevice, devices.Last["Name"].ToString());
            dirty = true;
        }

        var selectedDeviceName = selectedDevice.Value.ToString();
        if (!TrySelectObject(devices, $"$.[?(@.Name =~ /^{Regex.Escape(selectedDeviceName)}$/i)]", out var device))
        {
            Logger.Warn("Unable to find device! [SelectedDevice: \"{0}\"]", selectedDeviceName);
            device = devices.Last as JObject;
            SetProperty(selectedDevice, devices["Name"].ToString());
            dirty = true;
        }

        Logger.Debug("Initializing from device \"{0}\"", device["Name"].ToString());
        DeviceAxis.InitializeFromDevice(device.ToObject<DeviceSettings>());
        return dirty;
    }
}