namespace MultiFunPlayer.Input;

internal interface IShortcutActionConfigurationBuilder
{
    IShortcutActionConfiguration Build();
}

internal sealed class ShortcutActionConfigurationBuilder(string actionName, params IShortcutSettingBuilder[] builders) : IShortcutActionConfigurationBuilder
{
    public IShortcutActionConfiguration Build() => new ShortcutActionConfiguration(actionName, builders.Select(b => b.Build()));
}