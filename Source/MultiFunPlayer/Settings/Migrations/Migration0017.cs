using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0017 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        foreach (var output in SelectObjects(settings, "$.OutputTarget.Items[?(@.$type =~ /.*FileOutputTargetViewModel.*/i)]"))
        {
            if (!TryGetValue<JArray>(output, "EnabledAxes", out var enabledAxes)
             || !TryGetValue<JObject>(output, "AxisSettings", out var axisSettings))
             continue;

            var enabledAxesValues = enabledAxes.ToObject<List<string>>() ?? [];
            foreach (var property in axisSettings.Properties())
                SetPropertyByName(property.Value as JObject, "Enabled", enabledAxesValues.Contains(property.Name), addIfMissing: true);

            RemovePropertyByName(output, "EnabledAxes");
        }
    }
}