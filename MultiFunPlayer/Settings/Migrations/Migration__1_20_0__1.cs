using MultiFunPlayer.Common;
using MultiFunPlayer.OutputTarget.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__1_20_0__1 : AbstractConfigMigration
{
    private Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public override int TargetVersion => 3;

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

        var nameToTypeMap = new Dictionary<string, Type>()
        {
            ["Buttplug.io"] = typeof(ButtplugOutputTargetViewModel),
            ["Network"] = typeof(NetworkOutputTargetViewModel),
            ["Pipe"] = typeof(PipeOutputTargetViewModel),
            ["Serial"] = typeof(SerialOutputTargetViewModel)
        };

        var items = new List<JObject>();
        foreach (var (name, type) in nameToTypeMap)
        {
            if (!settings.ContainsKey(name))
                continue;

            var o = settings[name] as JObject;

            settings.Remove(name);
            o.AddTypeProperty(type);
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
