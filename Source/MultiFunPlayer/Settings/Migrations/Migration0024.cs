using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0024 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        MigrateEndpointPropertiesToUri(settings);

        if (settings.TryGetObject(out var shortcutSettings, "Shortcuts"))
            MigrateEndpointActionConfigurations(shortcutSettings);

        base.Migrate(settings);
    }

    private void MigrateEndpointPropertiesToUri(JObject settings)
    {
        Logger.Info("Migrating endpoint properties to uri");
        var migrations = new Dictionary<string, string>()
        {
            ["$.MediaSource.Emby.ServerEndpoint"] = "ServerBaseUri",
            ["$.MediaSource.Jellyfin.ServerEndpoint"] = "ServerBaseUri",
            ["$.MediaSource.Plex.ServerEndpoint"] = "ServerBaseUri",
            ["$.Script.Repositories.Stash.Endpoint"] = "ServerBaseUri",
            ["$.Script.Repositories.XBVR.Endpoint"] = "ServerBaseUri",
        };

        foreach(var (path, newName) in migrations)
        {
            if (settings.SelectToken(path) is not JValue token)
                continue;
            if (token.Parent is not JProperty property)
                continue;
            if (property.Parent is not JObject parent)
                continue;

            if (!NetUtils.TryParseEndpoint(token.Value<string>(), out var endpoint))
                continue;

            var oldValue = endpoint.ToUriString();
            var newValue = $"http://{oldValue}";
            property.Value = newValue;
            Logger.Info("Changed \"{0}\" value from \"{1}\" to \"{2}\"", path, oldValue, newValue);

            var oldName = property.Name;
            parent.RenameProperty(oldName, newName);
            Logger.Info("Renamed \"{0}\" property from \"{1}\" to \"{2}\"", path, oldName, newName);
        }
    }

    private void MigrateEndpointActionConfigurations(JObject settings)
    {
        Logger.Info("Migrating endpoint action configurations");

        var migrations = new Dictionary<string, string>()
        {
            ["Plex::Endpoint::Set"] = "Plex::ServerBaseUri::Set",
            ["Emby::Endpoint::Set"] = "Emby::ServerBaseUri::Set",
            ["Jellyfin::Endpoint::Set"] = "Jellyfin::ServerBaseUri::Set",
        };

        foreach (var (oldName, newName) in migrations)
        {
            foreach(var action in settings.SelectTokens($"$.Bindings[*].Actions[?(@.Name == '{oldName}')]").OfType<JObject>())
            {
                action["Name"] = newName;
                Logger.Info("Changed \"{0}\" name from \"{1}\" to \"{2}\"", action.Path, oldName, newName);

                var valueToken = action["Settings"][0]["Value"] as JValue;
                var oldValue = valueToken.Value<string>();
                var newValue = $"http://{oldValue}";
                valueToken.Value = newValue;
                Logger.Info("Changed \"{0}\" value from \"{1}\" to \"{2}\"", valueToken.Path, oldValue, newValue);
            }
        }
    }
}