using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0030 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        foreach (var funscriptNames in SelectArrays(settings, "$.Devices[?(@.IsDefault == false)].Axes[?(@.Name == 'L0')].FunscriptNames"))
            if (!funscriptNames.ToObject<List<string>>().Contains("raw"))
                AddTokenToContainer("raw", funscriptNames);

        base.Migrate(settings);
    }
}
