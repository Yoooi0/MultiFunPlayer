using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0007 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var axisSettings, "Script", "AxisSettings"))
            MigrateAxisSettings(axisSettings);

        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateInvertedActions(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateAxisSettings(JObject settings)
    {
        Logger.Info("Migrating Axis Settings");
        foreach (var (axis, child) in settings)
        {
            if (child is not JObject axisSettings)
                continue;

            if (axisSettings.RenameProperty("Inverted", "InvertScript"))
                Logger.Info("Migrated {0} setting from \"Inverted\" to \"InvertScript\"", axis);

            if (axisSettings.RenameProperty("Scale", "ScriptScale"))
                Logger.Info("Migrated {0} setting from \"Scale\" to \"ScriptScale\"", axis);
        }
    }

    private void MigrateInvertedActions(JObject settings)
    {
        Logger.Info("Migrating Inverted Actions");
        foreach (var action in settings.SelectTokens("$.Bindings[*].Actions[?(@.Descriptor =~ /Axis::Inverted::.*/i)]").OfType<JObject>())
        {
            var oldDescriptor = action["Descriptor"].ToString();
            var newDescriptor = oldDescriptor.Replace("Axis::Inverted::", "Axis::InvertScript::");

            action["Descriptor"] = newDescriptor;
            Logger.Info("Migrated action descriptor from \"{0}\" to \"{1}\"", oldDescriptor, newDescriptor);
        }
    }
}