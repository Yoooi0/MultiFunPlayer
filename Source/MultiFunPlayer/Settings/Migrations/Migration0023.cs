using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0023 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var scriptSettings, "Script"))
            MigrateLocalRepository(scriptSettings);

        base.Migrate(settings);
    }

    private void MigrateLocalRepository(JObject settings)
    {
        Logger.Info("Migrating local script repository Enabled property");
        if (!settings.TryGetObject(out var localRepository, "Repositories", "Local"))
            return;

        if (!localRepository.ContainsKey("Enabled"))
            return;

        localRepository["Enabled"] = true;
        Logger.Info("Forced local script repository Enabled property to \"true\"");
    }
}