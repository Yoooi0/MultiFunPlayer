using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

public interface IConfigMigration
{
    int TargetVersion { get; }
    void Migrate(JObject settings);
}