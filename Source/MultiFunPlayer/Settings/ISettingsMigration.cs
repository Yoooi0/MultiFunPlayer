﻿using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

internal interface ISettingsMigration
{
    int TargetVersion { get; }
    void Migrate(JObject settings);
}

internal abstract class AbstractSettingsMigration : JsonEditor, ISettingsMigration
{
    public int TargetVersion { get; }
    protected Logger Logger { get; }

    protected AbstractSettingsMigration()
    {
        TargetVersion = int.Parse(GetType().Name[^4..]);
        Logger = LogManager.GetLogger(GetType().FullName);
    }

    protected abstract void InternalMigrate(JObject settings);

    protected override void Log(LogLevel level, string message, params object[] args)
        => Logger.Log(level, message, args);

    public void Migrate(JObject settings)
    {
        Logger.Info("Migrating settings to version {0}", TargetVersion);
        InternalMigrate(settings);
        SetPropertyByName(settings, "ConfigVersion", TargetVersion, addIfMissing: true);
    }
}