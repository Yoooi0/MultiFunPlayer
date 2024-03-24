using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0018 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        MigrateInvertScriptAxisSettings(settings);

        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateBypassActions(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateInvertScriptAxisSettings(JObject settings)
    {
        Logger.Info("Migrating invert settings");

        foreach (var axisSettings in settings.SelectTokens("$.Script.AxisSettings.*").OfType<JObject>())
        {
            if (!axisSettings.TryGetValue<bool>("InvertScript", out var invert))
                continue;

            axisSettings.RenameProperty("InvertScript", "InvertValue");
            Logger.Info($"Migrated \"InvertScript={invert}\" to \"InvertValue={invert}\"");
        }
    }

    private void MigrateBypassActions(JObject settings)
    {
        Logger.Info("Migrating invert actions");
        foreach (var action in settings.SelectTokens("$.Bindings[*].Actions[?(@.Descriptor =~ /Axis::InvertScript/i)]").OfType<JObject>())
        {
            var oldDescriptor = action["Descriptor"].ToString();
            var newDescriptor = oldDescriptor.Replace("Axis::InvertScript", "Axis::InvertValue");

            action["Descriptor"] = newDescriptor;
            Logger.Info("Migrated action descriptor from \"{0}\" to \"{1}\"", oldDescriptor, newDescriptor);
        }
    }
}