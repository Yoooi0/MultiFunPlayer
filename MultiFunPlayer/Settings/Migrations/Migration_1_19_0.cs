using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

public class Migration_1_19_0 : AbstractConfigMigration
{
    public override Version TargetVersion => new(1, 19, 0);

    protected override void OnMigrate(JObject settings) { }
}
