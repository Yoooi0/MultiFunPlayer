using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

internal abstract class AbstractConfigMigration : IConfigMigration
{
    public int TargetVersion { get; }

    protected abstract Logger Logger { get; }

    protected AbstractConfigMigration() => TargetVersion = int.Parse(GetType().Name[9..]);
    public virtual void Migrate(JObject settings) => settings["ConfigVersion"] = TargetVersion;

    protected bool RemoveProperty(JObject settings, string propertyPath)
    {
        try
        {
            var token = settings.SelectToken(propertyPath, errorWhenNoMatch: true);
            if (token is not JProperty property)
            {
                Logger.Warn("Selected token is not a property [Path: \"{0}\", Token: \"{1}\"]", propertyPath, token?.Type);
                return false;
            }

            property.Remove();
            Logger.Info("Removed property \"{0}\"", propertyPath);
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Failed to remove property \"{0}\"", propertyPath);
            return false;
        }
    }

    protected bool RemoveProperties(JObject settings, string propertyPath)
    {
        try
        {
            foreach (var property in settings.SelectTokens(propertyPath, errorWhenNoMatch: true).OfType<JProperty>())
            {
                property.Remove();
                Logger.Info("Removed property \"{0}\"", propertyPath);
            }

            return true;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Failed to remove properties \"{0}\"", propertyPath);
            return false;
        }
    }

    protected bool RemoveProperty(JObject settings, string objectPath, string propertyName)
    {
        try
        {
            var token = settings.SelectToken(objectPath, errorWhenNoMatch: true);
            if (token is not JObject o)
            {
                Logger.Warn("Selected token is not an object [Path: \"{0}\", Token: \"{1}\"]", objectPath, token?.Type);
                return false;
            }

            if (!o.ContainsKey(propertyName))
            {
                Logger.Warn("Object \"{0}\" is missing property \"{1}\"", objectPath, propertyName);
                return false;
            }

            o.Remove(propertyName);
            Logger.Info("Removed property \"{0}\" from \"{1}\"", propertyName, objectPath);
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Failed to remove property \"{0}\" from \"{1}\"", propertyName, objectPath);
            return false;
        }
    }

    protected bool RemoveProperties(JObject settings, string objectPath, params string[] propertyNames)
        => RemoveProperties(settings, objectPath, propertyNames.AsEnumerable());

    protected bool RemoveProperties(JObject settings, string objectPath, IEnumerable<string> propertyNames)
    {
        try
        {
            var token = settings.SelectToken(objectPath, errorWhenNoMatch: true);
            if (token is not JObject o)
            {
                Logger.Warn("Selected token is not an object [Path: \"{0}\", Token: \"{1}\"]", objectPath, token?.Type);
                return false;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!o.ContainsKey(propertyName))
                {
                    Logger.Warn("Object \"{0}\" is missing property \"{1}\"", objectPath, propertyName);
                    continue;
                }

                o.Remove(propertyName);
                Logger.Info("Removed property \"{0}\" from \"{1}\"", propertyName, objectPath);
            }

            return true;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Failed to remove properties \"{list}\" from \"{path}\"", propertyNames, objectPath);
            return false;
        }
    }
}