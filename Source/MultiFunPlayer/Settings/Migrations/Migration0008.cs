﻿using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0008 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertyByPath(settings,
            "$.LogBlacklist.['MultiFunPlayer.UI.Controls.ViewModels.ShortcutViewModel']",
            "MultiFunPlayer.UI.Controls.ViewModels.ShortcutSettingsViewModel");
    }
}