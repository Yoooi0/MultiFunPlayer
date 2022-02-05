using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

public interface IConfigMigration
{
    Version TargetVersion { get; }

    bool CanMigrateTo(Version version);
    bool Migrate(JObject settings);
}