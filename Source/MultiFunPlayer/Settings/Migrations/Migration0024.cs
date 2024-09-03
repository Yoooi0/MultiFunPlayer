using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0024 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        var migrations = new Dictionary<string, string>()
        {
            ["$.MediaSource.Emby.ServerEndpoint"] = "ServerBaseUri",
            ["$.MediaSource.Jellyfin.ServerEndpoint"] = "ServerBaseUri",
            ["$.MediaSource.Plex.ServerEndpoint"] = "ServerBaseUri",
            ["$.Script.Repositories.Stash.Endpoint"] = "ServerBaseUri",
            ["$.Script.Repositories.XBVR.Endpoint"] = "ServerBaseUri",
        };

        EditPropertiesByPaths(settings, migrations.Keys,
            v => NetUtils.TryParseEndpoint(v.ToString(), out var endpoint) ? $"http://{endpoint.ToUriString()}" : null);
        RenamePropertiesByPaths(settings, migrations, selectMultiple: false);

        EditPropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Name =~ /(Plex|Emby|Jellyfin)::Endpoint::Set/i)].Name",
            v => v.ToString().Replace("Endpoint", "ServerBaseUri"));

        EditPropertiesByPath(settings, "$.Shortcuts.Bindings[*].Actions[?(@.Name =~ /(Plex|Emby|Jellyfin)::ServerBaseUri::Set/i)].Settings[0].Value",
            v => NetUtils.TryParseEndpoint(v.ToString(), out var endpoint) ? $"http://{endpoint.ToUriString()}" : null);
    }
}