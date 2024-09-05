using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0043 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        EditPropertiesByPaths(settings, [
            "$.Script.Repositories.Stash.ApiKey",
            "$.MediaSource.Plex.PlexToken",
            "$.MediaSource.Jellyfin.ApiKey",
            "$.MediaSource.Emby.ApiKey",
            "$.OutputTarget.Items[?(@.$type=~ /MultiFunPlayer.OutputTarget.ViewModels.TheHandyOutputTarget.*/i)].ConnectionKey"
        ], v => v.Type switch
        {
            JTokenType.String => ProtectedStringUtils.Protect(v.ToObject<string>(),
                                    e => Logger.Warn(e, "Failed to encrypt \"{0}\" [Path: \"{1}\"]", v.ToString(), v.Path)),
            _ => v
        });
    }
}