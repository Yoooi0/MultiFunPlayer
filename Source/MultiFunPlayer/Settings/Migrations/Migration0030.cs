using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0030 : AbstractSettingsMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPath(settings, "$.Devices[?(@.IsDefault == false)].Axes[?(@.Name == 'L0')].FunscriptNames", v =>
        {
            var funscriptNames = v.ToObject<List<string>>();
            if (funscriptNames.Contains("raw"))
                return v;

            funscriptNames.Add("raw");
            return JArray.FromObject(funscriptNames);
        });
    }
}
