namespace MultiFunPlayer.Common;

public static class EnumUtils
{
    public static T[] GetValues<T>() where T : Enum
        => (T[])Enum.GetValues(typeof(T));

    public static Dictionary<TEnum, TValue> ToDictionary<TEnum, TValue>(Func<TEnum, TValue> valueGenerator) where TEnum : Enum
        => GetValues<TEnum>().ToDictionary(x => x, valueGenerator);
}
