using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0020 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (TrySelectProperty(settings, "$.Shortcuts.Bindings[*].Gesture[?(@.$type =~ /.*GamepadButtonGestureDescriptor.*/i)].Button", out var property))
            if (RenameProperty(ref property, "Buttons"))
                SetProperty(property, new JArray(property.Value.ToString()));

        base.Migrate(settings);
    }
}