using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0031 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        foreach (var axisSettings in SelectObjects(settings, "$.Devices[?(@.IsDefault == false)].Axes[*]"))
        {
            var loadUnnamed = GetValue<JValue>(axisSettings, "LoadUnnamedScript").ToObject<bool>();
            var axisName = GetValue<JValue>(axisSettings, "Name").ToObject<string>();
            var funscriptNames = GetValue<JArray>(axisSettings, "FunscriptNames").ToObject<List<string>>();
            var newFunscriptNames = new List<string>();

            RemovePropertyByName(axisSettings, "LoadUnnamedScript");

            if (string.Equals(axisName, "L0", StringComparison.OrdinalIgnoreCase))
            {
                void AddIfContains(string funscriptName)
                {
                    if (funscriptNames.RemoveAll(x => string.Equals(x, funscriptName, StringComparison.OrdinalIgnoreCase)) > 0)
                        newFunscriptNames.Add(funscriptName);
                }

                AddIfContains("raw");
                if (loadUnnamed)
                    newFunscriptNames.Add("*");

                AddIfContains("stroke");
                AddIfContains("L0");
                AddIfContains("up");

                newFunscriptNames.InsertRange(0, funscriptNames.AsEnumerable().Reverse());
            }
            else if (loadUnnamed)
            {
                newFunscriptNames.AddRange(funscriptNames);
                newFunscriptNames.Add("*");
            }
            else
            {
                continue;
            }

            if (!newFunscriptNames.SequenceEqual(funscriptNames, StringComparer.OrdinalIgnoreCase))
                SetPropertyByName(axisSettings, "FunscriptNames", JArray.FromObject(newFunscriptNames));
        }
    }
}
