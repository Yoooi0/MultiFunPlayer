using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal class Migration0005 : AbstractConfigMigration
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        MigrateVideoPathModifierTypes(settings);
        MigrateUriToLocalMediaPathModifierSettings(settings);

        if (settings.TryGetObject(out var scriptSettings, "Script"))
        {
            MigrateScriptSettings(scriptSettings);

            if (scriptSettings.TryGetObject(out var syncSettings, "SyncSettings"))
                MigrateSyncSettings(syncSettings);
        }

        MigrateMediaActionDescriptors(settings);
        MigrateVideoSourceSettings(settings);

        base.Migrate(settings);
    }

    private void MigrateUriToLocalMediaPathModifierSettings(JObject settings)
    {
        Logger.Info("Migrating UriToLocalMediaPathModifierViewModel Settings");

        foreach (var modifier in settings.SelectTokens("$.Script.VideoPathModifiers[?(@.$type =~ /.*UriToLocalMediaPathModifierViewModel.*/i)]").OfType<JObject>())
            if (modifier.RenameProperty("VideoDirectory", "MediaDirectory"))
                Logger.Info("Migrated from \"VideoDirectory\" to \"MediaDirectory\"");
    }

    private void MigrateVideoSourceSettings(JObject settings)
    {
        Logger.Info("Migrating VideoSource Settings");

        if (settings.RenameProperty("VideoSource", "MediaSource"))
            Logger.Info("Migrated from \"VideoSource\" to \"MediaSource\"");
    }

    private void MigrateScriptSettings(JObject settings)
    {
        Logger.Info("Migrating Script Settings");

        if (settings.RenameProperty("VideoPathModifiers", "MediaPathModifiers"))
            Logger.Info("Migrated from \"VideoPathModifiers\" to \"MediaPathModifiers\"");

        if (settings.RenameProperty("VideoContentVisible", "MediaContentVisible"))
            Logger.Info("Migrated from \"VideoContentVisible\" to \"MediaContentVisible\"");
    }

    private void MigrateSyncSettings(JObject settings)
    {
        Logger.Info("Migrating Sync Settings");

        if (settings.RenameProperty("SyncOnVideoFileChanged", "SyncOnMediaFileChanged"))
            Logger.Info("Migrated from \"SyncOnVideoFileChanged\" to \"SyncOnMediaFileChanged\"");

        if (settings.RenameProperty("SyncOnVideoPlayPause", "SyncOnMediaPlayPause"))
            Logger.Info("Migrated from \"SyncOnVideoPlayPause\" to \"SyncOnMediaPlayPause\"");
    }

    private void MigrateVideoPathModifierTypes(JObject settings)
    {
        Logger.Info("Migrating Video Path Modifier Types");

        foreach (var videoPathModifier in settings.SelectTokens("$.Script.VideoPathModifiers[*]"))
        {
            var oldType = videoPathModifier["$type"].ToString();
            var newType = Regex.Replace(oldType, @"^MultiFunPlayer\.VideoSource", "MultiFunPlayer.MediaSource");

            videoPathModifier["$type"] = newType;
            Logger.Info("Migrated video path modifier type from \"{0}\" to \"{1}\"", oldType, newType);
        }
    }

    private void MigrateMediaActionDescriptors(JObject settings)
    {
        Logger.Info("Migrating Media Action Descriptors");

        foreach (var action in settings.SelectTokens("$.Shortcuts.Bindings[*].Actions[?(@.Descriptor =~ /Video::.*/i)]"))
        {
            var oldDescriptor = action["Descriptor"].ToString();
            var newDescriptor = Regex.Replace(oldDescriptor, "^Video::", "Media::");

            action["Descriptor"] = newDescriptor;
            Logger.Info("Migrated action descriptor from \"{0}\" to \"{1}\"", oldDescriptor, newDescriptor);
        }
    }
}
