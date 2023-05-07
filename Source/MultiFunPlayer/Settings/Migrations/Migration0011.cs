using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal class Migration0011 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var outputTargetSettings, "OutputTarget"))
        {
            if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
                MigrateNetworkOutputTargetActions(shortcutSettings, outputTargetSettings);

            MigrateOutputTargetActiveItem(outputTargetSettings);
            MigrateNetworkOutputTargets(outputTargetSettings);
        }

        base.Migrate(settings);
    }

    private void MigrateNetworkOutputTargetActions(JObject shortcutSettings, JObject outputTargetSettings)
    {
        Logger.Info("Migrating OutputTarget Actions");

        var descriptorPrefixMap = new Dictionary<string, string>();
        foreach (var outputTarget in outputTargetSettings.SelectTokens("$.Items[?(@.$type =~ /.*NetworkOutputTargetViewModel.*/i)]").OfType<JObject>())
        {
            var index = outputTarget["$index"].ToObject<int>();
            var protocol = outputTarget["Protocol"].ToString();

            descriptorPrefixMap.Add($"Network/{index}", $"{protocol.ToUpper()}/{index}");
        }

        foreach (var action in shortcutSettings.SelectTokens("$.Bindings[*].Actions[?(@.Descriptor =~ /Network\\/\\d+::*/i)]").OfType<JObject>())
        {
            var oldDescriptor = action["Descriptor"].ToString();

            var descriptorPrefix = oldDescriptor.Split("::").First();
            var newDescriptor = oldDescriptor.Replace(descriptorPrefix, descriptorPrefixMap[descriptorPrefix]);

            action["Descriptor"] = newDescriptor;
            Logger.Info("Migrated action descriptor from \"{0}\" to \"{1}\"", oldDescriptor, newDescriptor);
        }
    }

    private void MigrateOutputTargetActiveItem(JObject settings)
    {
        Logger.Info("Migrating OutputTarget ActiveItem");
        if (!settings.ContainsKey("ActiveItem"))
            return;

        var activeItem = settings["ActiveItem"].ToString();
        if (!activeItem.StartsWith("Network"))
            return;

        var index = int.Parse(activeItem.Split('/')[1]);
        if (settings.SelectToken($"$.Items[?(@.$type =~ /.*NetworkOutputTargetViewModel.*/i && @.$index == {index})]") is JObject outputTarget)
        {
            var newActiveItem = $"{outputTarget["Protocol"].ToString().ToUpper()}/{index}";
            settings["ActiveItem"] = newActiveItem;
            Logger.Info("Migrated ActiveItem from \"{0}\" to \"{1}\"", activeItem, newActiveItem);
        }
    }

    private void MigrateNetworkOutputTargets(JObject settings)
    {
        Logger.Info("Migrating Network OutputTarget");

        foreach (var outputTarget in settings.SelectTokens("$.Items[?(@.$type =~ /.*NetworkOutputTargetViewModel.*/i)]").OfType<JObject>())
        {
            var type = outputTarget["$type"].ToString();
            var protocol = outputTarget["Protocol"].ToString();
            outputTarget.Remove("Protocol");

            var newType = type.Replace("Network", protocol);
            outputTarget["$type"] = newType;
            Logger.Info("Migrated OutputTarget from \"{0}\" to \"{1}\"", type, newType);

            var sendDirtyValuesOnly = protocol == "Tcp";
            outputTarget["SendDirtyValuesOnly"] = sendDirtyValuesOnly;
            Logger.Info("Migrated SendDirtyValuesOnly to \"{0}\"", sendDirtyValuesOnly);
        }
    }
}