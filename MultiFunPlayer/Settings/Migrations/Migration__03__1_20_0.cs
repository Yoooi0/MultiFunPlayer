using MultiFunPlayer.Common;
using MultiFunPlayer.OutputTarget.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__03__1_20_0 : AbstractConfigMigration
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (settings.TryGetObject(out var outputTargetSettings, "OutputTarget"))
        {
            MigrateOutputTargets(outputTargetSettings);
            MigrateActiveOutputTarget(outputTargetSettings);
        }

        base.Migrate(settings);
    }

    private void MigrateOutputTargets(JObject settings)
    {
        Logger.Info("Migrating OutputTargets");

        var nameToTypeMap = new Dictionary<string, string>()
        {
            ["Buttplug.io"] = "MultiFunPlayer.OutputTarget.ViewModels.ButtplugOutputTargetViewModel, MultiFunPlayer",
            ["Network"] = "MultiFunPlayer.OutputTarget.ViewModels.NetworkOutputTargetViewModel, MultiFunPlayer",
            ["Pipe"] = "MultiFunPlayer.OutputTarget.ViewModels.PipeOutputTargetViewModel, MultiFunPlayer",
            ["Serial"] = "MultiFunPlayer.OutputTarget.ViewModels.SerialOutputTargetViewModel, MultiFunPlayer"
        };

        var items = new List<JObject>();
        foreach (var (name, type) in nameToTypeMap)
        {
            if (!settings.ContainsKey(name))
                continue;

            var o = settings[name] as JObject;

            settings.Remove(name);
            o["$type"] = type;
            o["$index"] = 0;

            items.Add(o);
            Logger.Info($"Moved \"{name}\" to \"Items\"");
        }

        settings["Items"] = JArray.FromObject(items);
    }

    private void MigrateActiveOutputTarget(JObject settings)
    {
        Logger.Info("Migrating OutputTarget ActiveItem");
        if (!settings.ContainsKey("ActiveItem"))
            return;

        Logger.Info($"Migrated ActiveItem=\"{settings["ActiveItem"]}\" to ActiveItem=\"{settings["ActiveItem"]}/0\"");
        settings["ActiveItem"] = $"{settings["ActiveItem"]}/0";
    }
}
