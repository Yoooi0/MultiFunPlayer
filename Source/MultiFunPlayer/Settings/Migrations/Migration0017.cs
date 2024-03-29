using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0017 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        foreach (var output in SelectObjects(settings, "$.OutputTarget.Items[?(@.$type =~ /.*FileOutputTargetViewModel.*/)]"))
        {
            if (!TryGetValue<JArray>(output, "EnabledAxes", out var enabledAxes)
             || !TryGetValue<JObject>(output, "AxisSettings", out var axisSettings))
             continue;

            var enabledAxesValues = enabledAxes.ToObject<List<string>>();
            if (enabledAxesValues == null || enabledAxesValues.Count == 0)
                continue;

            foreach (var property in axisSettings.Properties())
                SetPropertyByName(property.Value as JObject, "Enabled", enabledAxesValues.Contains(property.Name), addIfMissing: true);

            RemovePropertyByName(output, "EnabledAxes");
        }

        base.Migrate(settings);
    }
}