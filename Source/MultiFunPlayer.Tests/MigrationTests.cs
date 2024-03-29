using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Tests;

public class MigrationTests
{
    public static IEnumerable<Type> FindImplementations<T>() => FindImplementations(typeof(T));
    public static IEnumerable<Type> FindImplementations(Type type)
        => type.Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && ReflectionUtils.IsAssignableFromOrSubclass(type, t));

    public static IEnumerable<Type> MigrationTypes => FindImplementations<AbstractConfigMigration>();
    private static string[] DefaultMigrationProperties => ["ConfigVersion"];

    public static IEnumerable<object[]> ExpectedPropertiesOnMigrate
        => MigrationTypes.Select(t => new object[] { t, t.Name switch {
            "Migration0009" => [.. DefaultMigrationProperties, "SelectedDevice", "Devices"],
            _ => DefaultMigrationProperties,
        }});

    [Theory]
    [MemberData(nameof(ExpectedPropertiesOnMigrate))]
    public void MigrateOnEmptyObjectAddsExpectedProperties(Type migrationType, string[] expectedProperties)
    {
        var migration = (AbstractConfigMigration)Activator.CreateInstance(migrationType);
        var o = new JObject();
        migration.Migrate(o);

        Assert.Equal(expectedProperties.Length, o.Count);
        foreach (var property in expectedProperties)
            Assert.True(o.ContainsKey(property));

        Assert.Equal(migration.TargetVersion, o["ConfigVersion"].ToObject<int>());
    }
}
