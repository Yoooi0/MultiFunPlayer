using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0019 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateActionNameProperty(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateActionNameProperty(JObject settings)
    {
        Logger.Info("Migrating action properties");
        foreach (var action in settings.SelectTokens("$.Bindings[*].Actions[*]").OfType<JObject>())
        {
            action.RenameProperty("Descriptor", "Name");
            Logger.Info("Renamed action property from \"Descriptor\" to \"Name\"");
        }
    }
}