using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0023 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (TrySelectObject(settings, "$.Script.Repositories.Local", out var localRepository))
            SetPropertyByName(localRepository, "Enabled", true, addIfMissing: true);

        base.Migrate(settings);
    }
}