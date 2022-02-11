using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration__1_19_0__1 : AbstractConfigMigration
{
    public override int TargetVersion => 1;

    public override void Migrate(JObject settings)
    {
        base.Migrate(settings);
    }
}
