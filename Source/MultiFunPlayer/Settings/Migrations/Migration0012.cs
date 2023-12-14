using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0012 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateFloatActionSettings(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateFloatActionSettings(JObject settings)
    {
        Logger.Info("Migrating action settings");

        foreach (var token in settings.SelectTokens("$.Bindings[*].Actions[*].Settings[?(@.$type == 'System.Single, System.Private.CoreLib')]"))
        {
            token["$type"] = "System.Double, System.Private.CoreLib";
            Logger.Info("Migrated setting type from \"System.Single\" to \"System.Double\"");
        }
    }
}