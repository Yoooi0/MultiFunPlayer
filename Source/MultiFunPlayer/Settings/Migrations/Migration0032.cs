using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0032 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (TryGetValue<JValue>(settings, "SelectedDevice", out var selectedDeviceName)
         && TrySelectObject(settings, $"$.Devices[?(@.IsDefault == false && @.Name =~ /{Regex.Escape(selectedDeviceName.ToString())}/i)]", out var selectedDevice)
         && TrySelectObjects(settings, "$.Devices[?(@.IsDefault == true)]", out var defaultDevices))
        {
            Logger.Info("Preparing selected device \"{0}\" for compare", selectedDeviceName);
            var selectedDeviceCopy = selectedDevice.DeepClone() as JObject;
            RemovePropertiesByPaths(selectedDeviceCopy, ["$.Axes[*].Enabled", "$.Name", "$.IsDefault"]);

            foreach (var defaultDevice in defaultDevices)
            {
                Logger.Info("Preparing default device \"{0}\" for compare", defaultDevice["Name"]);
                var defaultDeviceCopy = defaultDevice.DeepClone() as JObject;
                RemovePropertiesByPaths(defaultDeviceCopy, ["$.Axes[*].Enabled", "$.Name", "$.IsDefault"]);

                if (!JToken.DeepEquals(selectedDeviceCopy, defaultDeviceCopy))
                    continue;

                Logger.Info("Selected device \"{0}\" matches default device \"{1}\"", selectedDeviceName, defaultDevice["Name"]);
                SetPropertyByName(settings, "SelectedDevice", defaultDevice["Name"].ToString());
                foreach (var axisSettings in SelectObjects(selectedDevice, "$.Axes[*]"))
                    SetPropertyByPath(defaultDevice, $"$.Axes[?(@.Name == '{axisSettings["Name"]}')].Enabled",
                        axisSettings["Enabled"].ToObject<bool>());

                RemoveToken(selectedDevice);
                break;
            }
        }

        base.Migrate(settings);
    }
}
