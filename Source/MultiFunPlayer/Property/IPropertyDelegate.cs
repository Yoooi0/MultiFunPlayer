namespace MultiFunPlayer.Property;

internal interface IPropertyDelegate
{
    IReadOnlyList<Type> Arguments { get; }

    object GetValue(params object[] arguments);
}

internal interface IPropertyDelegate<TOut> : IPropertyDelegate
{
    new TOut GetValue(params object[] arguments);
    object IPropertyDelegate.GetValue(params object[] arguments) => GetValue(arguments);
}

internal abstract class AbstractPropertyDelegate<TOut> : IPropertyDelegate<TOut>
{
    private IReadOnlyList<Type> _arguments;

    public IReadOnlyList<Type> Arguments
    {
        get
        {
            _arguments ??= GetType().GetGenericArguments()[..^1].AsReadOnly();
            return _arguments;
        }
    }

    public abstract TOut GetValue(params object[] arguments);

    protected bool GetArgument<T>(object argument, out T value)
    {
        value = default;

        var result = argument == null ? !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null : argument is T;
        if (result)
            value = (T)argument;

        return result;
    }
}

internal class PropertyDelegate<TOut> : AbstractPropertyDelegate<TOut>
{
    private readonly Func<TOut> _getter;

    public PropertyDelegate(Func<TOut> getter) => _getter = getter;

    public override TOut GetValue(params object[] arguments)
    {
        if (arguments == null || arguments.Length == 0)
            throw new ArgumentException(null, nameof(arguments));

        return GetValue();
    }

    public TOut GetValue() => _getter();
}

internal class PropertyDelegate<T0, TOut> : AbstractPropertyDelegate<TOut>
{
    private readonly Func<T0, TOut> _getter;

    public PropertyDelegate(Func<T0, TOut> getter) => _getter = getter;

    public override TOut GetValue(params object[] arguments)
    {
        if (arguments?.Length != 1 || !GetArgument<T0>(arguments[0], out var arg0))
            throw new ArgumentException(null, nameof(arguments));

        return GetValue(arg0);
    }

    public TOut GetValue(T0 arg0) => _getter(arg0);
}

internal class PropertyDelegate<T0, T1, TOut> : AbstractPropertyDelegate<TOut>
{
    private readonly Func<T0, T1, TOut> _getter;

    public PropertyDelegate(Func<T0, T1, TOut> getter) => _getter = getter;

    public override TOut GetValue(params object[] arguments)
    {
        if (arguments?.Length != 2 || !GetArgument<T0>(arguments[0], out var arg0) || !GetArgument<T1>(arguments[1], out var arg1))
            throw new ArgumentException(null, nameof(arguments));

        return _getter(arg0, arg1);
    }

    public TOut GetValue(T0 arg0, T1 arg1) => _getter(arg0, arg1);
}