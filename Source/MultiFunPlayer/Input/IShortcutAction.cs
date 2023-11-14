using System;

namespace MultiFunPlayer.Input;

internal interface IShortcutAction
{
    void Invoke(params object[] arguments);
    void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGesture gesture);
    bool AcceptsGesture(IInputGestureDescriptor gestureDescriptor);
}

internal abstract class AbstractShortcutAction : IShortcutAction
{
    private readonly IReadOnlyList<Type> _arguments;

    protected AbstractShortcutAction()
    {
        _arguments ??= GetType().GetGenericArguments().AsReadOnly();
    }

    public abstract void Invoke(params object[] arguments);

    public void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGesture gesture)
    {
        if (_arguments.Count == 0)
            Invoke();
        else if (_arguments[0].IsAssignableTo(typeof(IInputGesture)))
            Invoke(actionConfiguration.GetActionParams(gesture));
        else
            Invoke(actionConfiguration.GetActionParams());
    }

    public bool AcceptsGesture(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor is ISimpleInputGestureDescriptor)
            return _arguments.Count == 0 || !_arguments[0].IsAssignableTo(typeof(IInputGesture)) || typeof(ISimpleInputGesture).IsAssignableTo(_arguments[0]);
        else if (gestureDescriptor is IAxisInputGestureDescriptor)
            return _arguments.Count > 0 && typeof(IAxisInputGesture).IsAssignableTo(_arguments[0]);

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

internal class ShortcutAction(Action action) : AbstractShortcutAction
{
    public override void Invoke(params object[] arguments)
    {
        if (arguments == null || arguments.Length == 0)
            Invoke();
    }

    public void Invoke() => action.Invoke();
}

internal class ShortcutAction<T0>(Action<T0> action) : AbstractShortcutAction
{
    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 1 && GetArgument<T0>(arguments[0], out var arg0))
            Invoke(arg0);
    }

    public void Invoke(T0 arg0) => action.Invoke(arg0);
}

internal class ShortcutAction<T0, T1>(Action<T0, T1> action) : AbstractShortcutAction
{
    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 2 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1))
            Invoke(arg0, arg1);
    }

    public void Invoke(T0 arg0, T1 arg1) => action.Invoke(arg0, arg1);
}

internal class ShortcutAction<T0, T1, T2>(Action<T0, T1, T2> action) : AbstractShortcutAction
{
    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 3 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2))
            Invoke(arg0, arg1, arg2);
    }

    public void Invoke(T0 arg0, T1 arg1, T2 arg2) => action.Invoke(arg0, arg1, arg2);
}

internal class ShortcutAction<T0, T1, T2, T3>(Action<T0, T1, T2, T3> action) : AbstractShortcutAction
{
    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 4 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3))
            Invoke(arg0, arg1, arg2, arg3);
    }

    public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3) => action.Invoke(arg0, arg1, arg2, arg3);
}

internal class ShortcutAction<T0, T1, T2, T3, T4>(Action<T0, T1, T2, T3, T4> action) : AbstractShortcutAction
{
    public override void Invoke(params object[] arguments)
    {
        if (arguments?.Length == 5 && GetArgument<T0>(arguments[0], out var arg0) && GetArgument<T1>(arguments[1], out var arg1) && GetArgument<T2>(arguments[2], out var arg2) && GetArgument<T3>(arguments[3], out var arg3) && GetArgument<T4>(arguments[4], out var arg4))
            Invoke(arg0, arg1, arg2, arg3, arg4);
    }

    public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => action.Invoke(arg0, arg1, arg2, arg3, arg4);
}