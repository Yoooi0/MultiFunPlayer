using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal class Migration__17__1_26_0 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var outputTargetSettings, "OutputTarget"))
            MigrateFileOutputSettings(outputTargetSettings);

        base.Migrate(settings);
    }

    private void MigrateFileOutputSettings(JObject settings)
    {
        Logger.Info("Migrating FileOutputTarget");

        foreach (var output in settings.SelectTokens("$.Items[?(@.$type =~ /.*FileOutputTargetViewModel.*/)]").OfType<JObject>())
        {
            if (!output.ContainsKey("EnabledAxes") || !output.ContainsKey("AxisSettings"))
                continue;

            if (output["EnabledAxes"] is not JArray enabledAxesToken
             || output["AxisSettings"] is not JObject axisSettingsToken)
                continue;

            var enabledAxes = enabledAxesToken.ToObject<List<string>>();
            if (enabledAxes == null || enabledAxes.Count == 0)
                continue;

            foreach (var (axisName, settingsToken) in axisSettingsToken)
            {
                var enabled = enabledAxes.Contains(axisName);
                (settingsToken as JObject)["Enabled"] = enabled;
                Logger.Info($"Set \"{axisName}\" axis settings as \"Enabled={enabled}\"");
            }

            output.Remove("EnabledAxes");
            Logger.Info("Removed \"EnabledAxes\" property");
        }
    }
}