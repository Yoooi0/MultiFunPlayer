namespace MultiFunPlayer.Input;

internal interface IShortcutAction
{
    IReadOnlyList<Type> Arguments { get; }

    void Invoke(params object[] arguments);
}

internal abstract class AbstractShortcutAction : IShortcutAction
{
    private IReadOnlyList<Type> _arguments;

    public IReadOnlyList<Type> Arguments
    {
        get
        {
            _arguments ??= GetType().GetGenericArguments().AsReadOnly();
            return _arguments;
        }
    }

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

    public ShortcutAction(Action action) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments == null || arguments.Length == 0)
            Invoke();
    }

    public void Invoke() => _action.Invoke();
}

internal class ShortcutAction<T0> : AbstractShortcutAction
{
    private readonly Action<T0> _action;

    public ShortcutAction(Action<T0> action) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 1 && GetArgument<T0>(arguments[0], out var arg0))
            Invoke(arg0);
    }

    public void Invoke(T0 arg0) => _action.Invoke(arg0);
}

internal class ShortcutAction<T0, T1> : AbstractShortcutAction
{
    private readonly Action<T0, T1> _action;

    public ShortcutAction(Action<T0, T1> action) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 2 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1))
            Invoke(arg0, arg1);
    }

    public void Invoke(T0 arg0, T1 arg1) => _action.Invoke(arg0, arg1);
}

internal class ShortcutAction<T0, T1, T2> : AbstractShortcutAction
{
    private readonly Action<T0, T1, T2> _action;

    public ShortcutAction(Action<T0, T1, T2> action) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 3 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2))
            Invoke(arg0, arg1, arg2);
    }

    public void Invoke(T0 arg0, T1 arg1, T2 arg2) => _action.Invoke(arg0, arg1, arg2);
}

internal class ShortcutAction<T0, T1, T2, T3> : AbstractShortcutAction
{
    private readonly Action<T0, T1, T2, T3> _action;

    public ShortcutAction(Action<T0, T1, T2, T3> action) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 4 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3))
            Invoke(arg0, arg1, arg2, arg3);
    }

    public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3) => _action.Invoke(arg0, arg1, arg2, arg3);
}

internal class ShortcutAction<T0, T1, T2, T3, T4> : AbstractShortcutAction
{
    private readonly Action<T0, T1, T2, T3, T4> _action;

    public ShortcutAction(Action<T0, T1, T2, T3, T4> action) => _action = action;

    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 5 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3) && GetArgument<T4>(arguments[4], out var arg4))
            Invoke(arg0, arg1, arg2, arg3, arg4);
    }

    public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => _action.Invoke(arg0, arg1, arg2, arg3, arg4);
}