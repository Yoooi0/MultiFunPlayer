namespace MultiFunPlayer.Input;

public interface IShortcutActionBuilder
{
    public IShortcutAction Build();
}

public interface INoSettingsShortcutActionBuilder : IShortcutActionBuilder
{
    public INoSettingsShortcutActionBuilder WithCallback(Action<IInputGesture> callback);
    public IOneSettingShortcutActionBuilder<T> WithSetting<T>(Action<IShortcutSettingBuilder<T>> configure);
}

public interface IOneSettingShortcutActionBuilder<T0> : IShortcutActionBuilder
{
    public IOneSettingShortcutActionBuilder<T0> WithCallback(Action<IInputGesture, T0> callback);
    public ITwoSettingsShortcutActionBuilder<T0, T1> WithSetting<T1>(Action<IShortcutSettingBuilder<T1>> configure);
}

public interface ITwoSettingsShortcutActionBuilder<T0, T1> : IShortcutActionBuilder
{
    public ITwoSettingsShortcutActionBuilder<T0, T1> WithCallback(Action<IInputGesture, T0, T1> callback);
    public IThreeSettingsShortcutActionBuilder<T0, T1, T2> WithSetting<T2>(Action<IShortcutSettingBuilder<T2>> configure);
}

public interface IThreeSettingsShortcutActionBuilder<T0, T1, T2> : IShortcutActionBuilder
{
    public IThreeSettingsShortcutActionBuilder<T0, T1, T2> WithCallback(Action<IInputGesture, T0, T1, T2> callback);
    public IFourSettingsShortcutActionBuilder<T0, T1, T2, T3> WithSetting<T3>(Action<IShortcutSettingBuilder<T3>> configure);
}

public interface IFourSettingsShortcutActionBuilder<T0, T1, T2, T3> : IShortcutActionBuilder
{
    public IFourSettingsShortcutActionBuilder<T0, T1, T2, T3> WithCallback(Action<IInputGesture, T0, T1, T2, T3> callback);
}

public class ShortcutBuilder : INoSettingsShortcutActionBuilder
{
    private readonly IShortcutActionDescriptor _descriptor;
    private Action<IInputGesture> _callback;

    public ShortcutBuilder(IShortcutActionDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public INoSettingsShortcutActionBuilder WithCallback(Action<IInputGesture> callback)
    {
        _callback = callback;
        return this;
    }

    public IOneSettingShortcutActionBuilder<T> WithSetting<T>(Action<IShortcutSettingBuilder<T>> configure)
    {
        var setting0 = new ShortcutSettingBuilder<T>();
        configure(setting0);
        return new ShortcutBuilder<T>(_descriptor, setting0);
    }

    public IShortcutAction Build() => new ShortcutAction(_descriptor, _callback);
}

public class ShortcutBuilder<T0> : IOneSettingShortcutActionBuilder<T0>
{
    private readonly IShortcutActionDescriptor _descriptor;
    private readonly IShortcutSettingBuilder<T0> _setting0;
    private Action<IInputGesture, T0> _callback;

    public ShortcutBuilder(IShortcutActionDescriptor descriptor, IShortcutSettingBuilder<T0> setting0)
    {
        _descriptor = descriptor;
        _setting0 = setting0;
    }

    public IOneSettingShortcutActionBuilder<T0> WithCallback(Action<IInputGesture, T0> callback)
    {
        _callback = callback;
        return this;
    }

    public ITwoSettingsShortcutActionBuilder<T0, T1> WithSetting<T1>(Action<IShortcutSettingBuilder<T1>> configure)
    {
        var setting1 = new ShortcutSettingBuilder<T1>();
        configure(setting1);
        return new ShortcutBuilder<T0, T1>(_descriptor, _setting0, setting1);
    }

    public IShortcutAction Build() => new ShortcutAction<T0>(_descriptor, _callback, _setting0.Build());
}

public class ShortcutBuilder<T0, T1> : ITwoSettingsShortcutActionBuilder<T0, T1>
{
    private readonly IShortcutActionDescriptor _descriptor;
    private readonly IShortcutSettingBuilder<T0> _setting0;
    private readonly IShortcutSettingBuilder<T1> _setting1;
    private Action<IInputGesture, T0, T1> _callback;

    public ShortcutBuilder(IShortcutActionDescriptor descriptor, IShortcutSettingBuilder<T0> setting0, IShortcutSettingBuilder<T1> setting1)
    {
        _descriptor = descriptor;
        _setting0 = setting0;
        _setting1 = setting1;
    }

    public ITwoSettingsShortcutActionBuilder<T0, T1> WithCallback(Action<IInputGesture, T0, T1> callback)
    {
        _callback = callback;
        return this;
    }

    public IThreeSettingsShortcutActionBuilder<T0, T1, T2> WithSetting<T2>(Action<IShortcutSettingBuilder<T2>> configure)
    {
        var setting2 = new ShortcutSettingBuilder<T2>();
        configure(setting2);
        return new ShortcutBuilder<T0, T1, T2>(_descriptor, _setting0, _setting1, setting2);
    }

    public IShortcutAction Build() => new ShortcutAction<T0, T1>(_descriptor, _callback, _setting0.Build(), _setting1.Build());
}

public class ShortcutBuilder<T0, T1, T2> : IThreeSettingsShortcutActionBuilder<T0, T1, T2>
{
    private readonly IShortcutActionDescriptor _descriptor;
    private readonly IShortcutSettingBuilder<T0> _setting0;
    private readonly IShortcutSettingBuilder<T1> _setting1;
    private readonly IShortcutSettingBuilder<T2> _setting2;
    private Action<IInputGesture, T0, T1, T2> _callback;

    public ShortcutBuilder(IShortcutActionDescriptor descriptor, IShortcutSettingBuilder<T0> setting0, IShortcutSettingBuilder<T1> setting1, IShortcutSettingBuilder<T2> setting2)
    {
        _descriptor = descriptor;
        _setting0 = setting0;
        _setting1 = setting1;
        _setting2 = setting2;
    }

    public IThreeSettingsShortcutActionBuilder<T0, T1, T2> WithCallback(Action<IInputGesture, T0, T1, T2> callback)
    {
        _callback = callback;
        return this;
    }

    public IFourSettingsShortcutActionBuilder<T0, T1, T2, T3> WithSetting<T3>(Action<IShortcutSettingBuilder<T3>> configure)
    {
        var setting3 = new ShortcutSettingBuilder<T3>();
        configure(setting3);
        return new ShortcutBuilder<T0, T1, T2, T3>(_descriptor, _setting0, _setting1, _setting2, setting3);
    }

    public IShortcutAction Build() => new ShortcutAction<T0, T1, T2>(_descriptor, _callback, _setting0.Build(), _setting1.Build(), _setting2.Build());
}

public class ShortcutBuilder<T0, T1, T2, T3> : IFourSettingsShortcutActionBuilder<T0, T1, T2, T3>
{
    private readonly IShortcutActionDescriptor _descriptor;
    private readonly IShortcutSettingBuilder<T0> _setting0;
    private readonly IShortcutSettingBuilder<T1> _setting1;
    private readonly IShortcutSettingBuilder<T2> _setting2;
    private readonly IShortcutSettingBuilder<T3> _setting3;
    private Action<IInputGesture, T0, T1, T2, T3> _callback;

    public ShortcutBuilder(IShortcutActionDescriptor descriptor, IShortcutSettingBuilder<T0> setting0, IShortcutSettingBuilder<T1> setting1, IShortcutSettingBuilder<T2> setting2, IShortcutSettingBuilder<T3> setting3)
    {
        _descriptor = descriptor;
        _setting0 = setting0;
        _setting1 = setting1;
        _setting2 = setting2;
        _setting3 = setting3;
    }

    public IFourSettingsShortcutActionBuilder<T0, T1, T2, T3> WithCallback(Action<IInputGesture, T0, T1, T2, T3> callback)
    {
        _callback = callback;
        return this;
    }

    public IShortcutAction Build() => new ShortcutAction<T0, T1, T2, T3>(_descriptor, _callback, _setting0.Build(), _setting1.Build(), _setting2.Build(), _setting3.Build());
}