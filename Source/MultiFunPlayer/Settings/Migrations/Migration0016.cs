using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal class Migration0016 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateBindings(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateBindings(JObject settings)
    {
        Logger.Info("Migrating bindings");
        if (!settings.ContainsKey("Bindings"))
            return;

        foreach (var binding in settings["Bindings"].Children<JObject>())
        {
            if (binding.ContainsKey("Enabled"))
                continue;

            binding.Add("Enabled", JToken.FromObject(true));
            Logger.Info("Added \"Enabled=true\" property to binding");
        }
    }
}