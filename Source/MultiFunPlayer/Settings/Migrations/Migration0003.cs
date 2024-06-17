using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0003 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        if (!TryGetValue<JObject>(settings, "OutputTarget", out var outputTargets))
            return;

        var nameToTypeMap = new Dictionary<string, string>()
        {
            ["Buttplug.io"] = "MultiFunPlayer.OutputTarget.ViewModels.ButtplugOutputTargetViewModel, MultiFunPlayer",
            ["Network"] = "MultiFunPlayer.OutputTarget.ViewModels.NetworkOutputTargetViewModel, MultiFunPlayer",
            ["Pipe"] = "MultiFunPlayer.OutputTarget.ViewModels.PipeOutputTargetViewModel, MultiFunPlayer",
            ["Serial"] = "MultiFunPlayer.OutputTarget.ViewModels.SerialOutputTargetViewModel, MultiFunPlayer"
        };

        AddPropertyByName(outputTargets, "Items", new JArray());

        var items = GetValue<JArray>(outputTargets, "Items");
        foreach (var property in GetProperties(outputTargets, nameToTypeMap.Keys))
        {
            var outputTarget = property.Value as JObject;
            AddPropertiesByName(outputTarget, new Dictionary<string, JToken>()
            {
                ["$type"] = nameToTypeMap[property.Name],
                ["$index"] = 0
            });

            AddTokenToContainer(outputTarget, items);
            RemoveProperty(property);
        }

        EditPropertyByName(outputTargets, "ActiveItem", v => $"{v}/0");
    }
}
