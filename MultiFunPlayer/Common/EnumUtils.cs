namespace MultiFunPlayer.Common;

public static class EnumUtils
{
    public static T[] GetValues<T>()
    {
        var nullableType = Nullable.GetUnderlyingType(typeof(T));
        var type = nullableType ?? typeof(T);
        if (!type.IsEnum)
            throw new ArgumentException($"Generic agrgument \"{nameof(T)}\" must be an enum type.");

        if (nullableType == null)
            return (T[])Enum.GetValues(type);

        var result = new List<T>();
        foreach (var value in Enum.GetValues(type))
            result.Add((T)value);

        return result.ToArray();
    }

    public static Dictionary<TEnum, TValue> ToDictionary<TEnum, TValue>(Func<TEnum, TValue> valueGenerator) where TEnum : Enum
        => GetValues<TEnum>().ToDictionary(x => x, valueGenerator);
}
