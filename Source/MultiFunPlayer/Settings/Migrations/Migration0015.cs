using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0015 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        MigrateBypassAxisSettings(settings);

        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateBypassActions(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateBypassAxisSettings(JObject settings)
    {
        Logger.Info("Migrating bypass settings");

        foreach (var axisSettings in settings.SelectTokens("$.Script.AxisSettings.*").OfType<JObject>())
        {
            if (!axisSettings.TryGetValue<bool>("Bypass", out var bypass))
                continue;

            axisSettings.Remove("Bypass");
            axisSettings.Add("BypassScript", JToken.FromObject(bypass));
            axisSettings.Add("BypassMotionProvider", JToken.FromObject(bypass));
            axisSettings.Add("BypassTransition", JToken.FromObject(bypass));

            Logger.Info($"Migrated \"Bypass={bypass}\" to \"BypassScript={bypass}\", \"BypassMotionProvider={bypass}\", \"BypassTransition={bypass}\"");
        }
    }

    private void MigrateBypassActions(JObject settings)
    {
        Logger.Info("Migrating bypass actions");
        foreach (var action in settings.SelectTokens("$.Bindings[*].Actions[?(@.Descriptor =~ /Axis::Bypass::.*/i)]").OfType<JObject>())
        {
            var oldDescriptor = action["Descriptor"].ToString();
            var newDescriptor = oldDescriptor.Replace("Axis::Bypass::", "Axis::Bypass::All::");

            action["Descriptor"] = newDescriptor;
            Logger.Info("Migrated action descriptor from \"{0}\" to \"{1}\"", oldDescriptor, newDescriptor);
        }
    }
}