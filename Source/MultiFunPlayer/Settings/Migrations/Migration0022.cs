using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0022 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var scriptSettings, "Script"))
            MigrateScriptLibraryProperty(scriptSettings);

        base.Migrate(settings);
    }

    private void MigrateScriptLibraryProperty(JObject settings)
    {
        Logger.Info("Migrating ScriptLibraries property");
        if (!settings.ContainsKey("ScriptLibraries"))
            return;

        var property = settings.Property("ScriptLibraries");
        property.Remove();

        settings.EnsureContainsObjects("Repositories", "Local", "ScriptLibraries");
        settings["Repositories"]["Local"]["ScriptLibraries"] = property.Value;
        Logger.Info("Moved ScriptLibraries property from \"Script.ScriptLibraries\" to \"Script.Repositories.Local.ScriptLibraries\"");
    }
}