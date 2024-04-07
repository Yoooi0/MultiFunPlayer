using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0023 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        if (!TrySelectObject(settings, "$.Script.Repositories.Local", out var localRepository))
            return;

        SetPropertyByName(localRepository, "Enabled", true, addIfMissing: true);
    }
}