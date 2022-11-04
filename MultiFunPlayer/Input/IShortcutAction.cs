namespace MultiFunPlayer.Input;

internal interface IShortcutAction
{
    IShortcutActionDescriptor Descriptor { get; }
    void Invoke(params object[] arguments);
}

internal abstract class AbstractShortcutAction : IShortcutAction
{
    public IShortcutActionDescriptor Descriptor { get; }

    protected AbstractShortcutAction(IShortcutActionDescriptor descriptor)
        => Descriptor = descriptor;

    public abstract void Invoke(params object[] arguments);

    protected bool GetArgument<T>(object argument, out T value)
    {
        value = default;

        var result = argument == null ? !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null : argument is T;
        if (result)
            value = (T)argument;

        return result;
    }
}

internal class ShortcutAction : AbstractShortcutAction
{
    private readonly Action _action;

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action action)
        : base(descriptor) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments == null || arguments.Length == 0)
            _action?.Invoke();
    }
}

internal class ShortcutAction<T0> : AbstractShortcutAction
{
    private readonly Action<T0> _action;

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<T0> action)
        : base(descriptor) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 1 && GetArgument<T0>(arguments[0], out var arg0))
            _action?.Invoke(arg0);
    }
}

internal class ShortcutAction<T0, T1> : AbstractShortcutAction
{
    private readonly Action<T0, T1> _action;

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<T0, T1> action)
        : base(descriptor) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 2 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1))
            _action?.Invoke(arg0, arg1);
    }
}

internal class ShortcutAction<T0, T1, T2> : AbstractShortcutAction
{
    private readonly Action<T0, T1, T2> _action;

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<T0, T1, T2> action)
        : base(descriptor) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 3 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2))
            _action?.Invoke(arg0, arg1, arg2);
    }
}

internal class ShortcutAction<T0, T1, T2, T3> : AbstractShortcutAction
{
    private readonly Action<T0, T1, T2, T3> _action;

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<T0, T1, T2, T3> action)
        : base(descriptor) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 4 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3))
            _action?.Invoke(arg0, arg1, arg2, arg3);
    }
}

internal class ShortcutAction<T0, T1, T2, T3, T4> : AbstractShortcutAction
{
    private readonly Action<T0, T1, T2, T3, T4> _action;

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<T0, T1, T2, T3, T4> action)
        : base(descriptor) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 5 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3) && GetArgument<T4>(arguments[4], out var arg4))
            _action?.Invoke(arg0, arg1, arg2, arg3, arg4);
    }
}