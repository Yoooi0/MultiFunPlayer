﻿using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0012 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        SetPropertiesByPath(settings,
            "$.Shortcuts.Bindings[*].Actions[*].Settings[?(@.$type == 'System.Single, System.Private.CoreLib')].$type",
            "System.Double, System.Private.CoreLib");
    }
}