using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__4__1_20_0 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
        {
            MigrateOutputTargetBindings(shortcutSettings);
        }

        base.Migrate(settings);
    }

    private void MigrateOutputTargetBindings(JObject settings)
    {
        Logger.Info("Migrating OutputTarget Bindings");

        var prefixes = new List<string>()
        {
            "Buttplug.io::", "Network::", "Pipe::", "Serial::"
        };

        if (!settings.ContainsKey("Bindings"))
            return;

        var bindings = settings["Bindings"].OfType<JObject>();
        foreach (var binding in bindings)
        {
            if (!binding.ContainsKey("Actions"))
                continue;

            var actions = binding["Actions"].OfType<JObject>();
            foreach (var action in actions)
            {
                if (!action.ContainsKey("Descriptor"))
                    continue;

                var descriptor = action["Descriptor"].ToString();
                var prefix = prefixes.Find(p => descriptor.StartsWith(p));
                if (prefix == null)
                    continue;

                action["Descriptor"] = $"{prefix[..^2]}/0::{descriptor[prefix.Length..]}";
                Logger.Info("Migrated action from \"{0}\" to \"{1}\"", descriptor, action["Descriptor"].ToString());
            }
        }
    }
}
