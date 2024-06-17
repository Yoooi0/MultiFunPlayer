using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0004 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        var regex = new Regex(@"^(Buttplug\.io|Network|Pipe|Serial)::");
        EditPropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[*].Descriptor",
            v => regex.IsMatch(v.ToString()),
            v => regex.Replace(v.ToString(), "$1/0::"));
    }
}
