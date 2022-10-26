using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__12__1_24_0 : AbstractConfigMigration
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
        foreach(var token in settings.SelectTokens("$.Bindings[*].Actions[*].Settings[?(@.$type == 'System.Single, System.Private.CoreLib')]"))
        {
            token["$type"] = "System.Double, System.Private.CoreLib";
            Logger.Info("Migrated from \"System.Single\" to \"System.Double\"");
        }
    }
}