using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0005 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPath(settings, "$.Script.VideoPathModifiers[*].$type",
            v => Regex.Replace(v.ToString(), @"^MultiFunPlayer\.VideoSource", "MultiFunPlayer.MediaSource"));

        EditPropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Video::.*/i)].Descriptor",
            v => Regex.Replace(v.ToString(), "^Video::", "Media::"));

        RenamePropertiesByPath(settings, "$.Script.VideoPathModifiers[?(@.$type =~ /.*UriToLocalMediaPathModifierViewModel.*/i)].VideoDirectory", "MediaDirectory");
        RenamePropertiesByPaths(settings, new Dictionary<string, string>()
        {
            ["$.Script.VideoPathModifiers"] = "MediaPathModifiers",
            ["$.Script.VideoContentVisible"] = "MediaContentVisible",
            ["$.Script.SyncSettings.SyncOnVideoFileChanged"] = "SyncOnMediaFileChanged",
            ["$.Script.SyncSettings.SyncOnVideoPlayPause"] = "SyncOnMediaPlayPause",
            ["$.VideoSource"] = "MediaSource"
        }, selectMultiple: false);
    }
}
