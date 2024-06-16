using MultiFunPlayer.Input;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutAction
{
    ValueTask Invoke(params object[] arguments);
    ValueTask Invoke(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData);
    bool AcceptsGestureData(Type gestureDataType);
}

internal abstract class AbstractShortcutAction : IShortcutAction
{
    private readonly IReadOnlyList<Type> _arguments;

    protected AbstractShortcutAction()
    {
        _arguments ??= GetType().GetGenericArguments().AsReadOnly();
    }

    public abstract ValueTask Invoke(params object[] arguments);

    public ValueTask Invoke(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData)
    {
        if (_arguments.Count == 0)
            return Invoke();
        else if (_arguments[0].IsAssignableTo(typeof(IInputGestureData)))
            return Invoke(actionConfiguration.GetActionParams(gestureData));
        else
            return Invoke(actionConfiguration.GetActionParams());
    }

    public bool AcceptsGestureData(Type gestureDataType)
    {
        if (gestureDataType == typeof(ISimpleInputGestureData))
            return _arguments.Count == 0 || !_arguments[0].IsAssignableTo(typeof(IInputGestureData)) || gestureDataType.IsAssignableTo(_arguments[0]);
        if (gestureDataType == typeof(IAxisInputGestureData))
            return _arguments.Count > 0 && gestureDataType.IsAssignableTo(_arguments[0]);

        return false;
    }

    protected bool GetArgument<T>(object argument, out T value)
    {
        value = default;

        var result = argument == null ? !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null : argument is T;
        if (result)
            value = (T)argument;

        return result;
    }
}

internal sealed class ShortcutAction(Func<ValueTask> action) : AbstractShortcutAction
{
    public override ValueTask Invoke(params object[] arguments)
    {
        if (arguments == null || arguments.Length == 0)
            return Invoke();
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke() => action.Invoke();
}

internal sealed class ShortcutAction<T0>(Func<T0, ValueTask> action) : AbstractShortcutAction
{
    public override ValueTask Invoke(params object[] arguments)
    {
        if (arguments?.Length == 1 && GetArgument<T0>(arguments[0], out var arg0))
            return Invoke(arg0);
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke(T0 arg0) => action.Invoke(arg0);
}

internal sealed class ShortcutAction<T0, T1>(Func<T0, T1, ValueTask> action) : AbstractShortcutAction
{
    public override ValueTask Invoke(params object[] arguments)
    {
        if (arguments?.Length == 2 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1))
            return Invoke(arg0, arg1);
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke(T0 arg0, T1 arg1) => action.Invoke(arg0, arg1);
}

internal sealed class ShortcutAction<T0, T1, T2>(Func<T0, T1, T2, ValueTask> action) : AbstractShortcutAction
{
    public override ValueTask Invoke(params object[] arguments)
    {
        if (arguments?.Length == 3 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2))
            return Invoke(arg0, arg1, arg2);
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke(T0 arg0, T1 arg1, T2 arg2) => action.Invoke(arg0, arg1, arg2);
}

internal sealed class ShortcutAction<T0, T1, T2, T3>(Func<T0, T1, T2, T3, ValueTask> action) : AbstractShortcutAction
{
    public override ValueTask Invoke(params object[] arguments)
    {
        if (arguments?.Length == 4 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3))
            return Invoke(arg0, arg1, arg2, arg3);
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3) => action.Invoke(arg0, arg1, arg2, arg3);
}

internal sealed class ShortcutAction<T0, T1, T2, T3, T4>(Func<T0, T1, T2, T3, T4, ValueTask> action) : AbstractShortcutAction
{
    public override ValueTask Invoke(params object[] arguments)
    {
        if (arguments?.Length == 5 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3) && GetArgument<T4>(arguments[4], out var arg4))
            return Invoke(arg0, arg1, arg2, arg3, arg4);
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => action.Invoke(arg0, arg1, arg2, arg3, arg4);
}