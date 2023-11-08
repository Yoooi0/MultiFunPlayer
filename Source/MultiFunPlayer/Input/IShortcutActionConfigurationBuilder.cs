namespace MultiFunPlayer.Input;

internal interface IShortcutActionConfigurationBuilder
{
    IShortcutActionConfiguration Build();
}

internal class ShortcutActionConfigurationBuilder : IShortcutActionConfigurationBuilder
{
    private readonly string _actionName;
    private readonly IShortcutSettingBuilder[] _builders;

    public ShortcutActionConfigurationBuilder(string actionName, params IShortcutSettingBuilder[] builders)
    {
        _actionName = actionName;
        _builders = builders;
    }

    public IShortcutActionConfiguration Build() => new ShortcutActionConfiguration(_actionName, _builders.Select(b => b.Build()));
}