using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__1_21_0__2 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public override int TargetVersion => 6;

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var axisSettings, "Script", "AxisSettings"))
            MigrateSmartLimitSettings(axisSettings);

        MigrateSmartLimitActions(settings);

        base.Migrate(settings);
    }

    private void MigrateSmartLimitSettings(JObject settings)
    {
        Logger.Info("Migrating Smart Limit Settings");
        foreach (var (axis, child) in settings)
        {
            if (child is not JObject axisSettings)
                continue;

            if (axisSettings.ContainsKey("SmartLimitEnabled"))
            {
                var oldValue = axisSettings["SmartLimitEnabled"].ToObject<bool>();
                var newValue = oldValue ? "L0" : null;

                if (!axisSettings.ContainsKey("SmartLimitInputAxis"))
                    axisSettings.Add("SmartLimitInputAxis", newValue);

                axisSettings.Remove("SmartLimitEnabled");
                Logger.Info("Migrated {0} setting from \"SmartLimitEnabled={1}\" to \"SmartLimitInputAxis={1}\"", axis, oldValue, newValue);
            }

            if (!axisSettings.ContainsKey("SmartLimitPoints"))
            {
                axisSettings.Add("SmartLimitPoints", JArray.FromObject(new string[] { "25,100", "90,0" }));
                Logger.Info("Added \"SmartLimitPoints\" to {0} settings", axis);
            }
        }
    }

    private void MigrateSmartLimitActions(JObject settings)
    {
        Logger.Info("Migrating Smart Limit Actions");
        foreach (var action in settings.SelectTokens("$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Axis::SmartLimitEnabled::Set.*/i)]").OfType<JObject>())
        {
            const string newDescriptor = "Axis::SmartLimitInputAxis::Set";
            var oldDescriptor = action["Descriptor"].ToString();

            action["Descriptor"] = newDescriptor;
            Logger.Info("Migrated action descriptor from \"{0}\" to \"{1}\"", oldDescriptor, newDescriptor);

            if (action.ContainsKey("Settings"))
            {
                var axisSettings = action["Settings"] as JArray;
                var oldSetting = axisSettings[1] as JObject;
                var oldValue = oldSetting["Value"].ToObject<bool>();
                var newValue = oldValue ? "L0" : null;

                var newSetting = new JObject();
                newSetting.AddTypeProperty(typeof(DeviceAxis));
                newSetting["Value"] = oldValue ? "L0" : null;

                axisSettings.RemoveAt(1);
                axisSettings.Add(newSetting);

                Logger.Info("Migrated action setting from \"{0}\" to \"{1}\"", oldValue, newValue);
            }
        }

        foreach (var action in settings.SelectTokens("$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Axis::SmartLimitEnabled::Toggle.*/i)]").ToList())
        {
            var parent = action.Parent as JArray;
            parent.Remove(action);

            Logger.Info("Removed \"Axis::SmartLimitEnabled::Toggle\" action");
        }
    }
}