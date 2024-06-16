using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0040 : AbstractSettingsMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPath(settings, "$.Devices[*].Axes[?(@.Name == 'L0')].FunscriptNames", v =>
        {
            var funscriptNames = v.ToObject<List<string>>();

            var rawIndex = funscriptNames.IndexOf("raw");
            if (rawIndex < 0)
                return v;

            var unnamedIndex = funscriptNames.IndexOf("*");
            if (unnamedIndex < 0)
                return v;

            if (rawIndex < unnamedIndex)
                funscriptNames.Remove("raw");

            return JArray.FromObject(funscriptNames);
        });
    }
}
