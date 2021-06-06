using System;
using System.Collections.Generic;
using System.Linq;

namespace MultiFunPlayer.Common
{
    public static class TCode
    {
        public static string ToString(DeviceAxis axis, float value) => $"{axis}{value * 999:000}";
        public static string ToString(DeviceAxis axis, float value, int interval) => $"{ToString(axis, value)}I{interval}";

        public static string ToString(IEnumerable<KeyValuePair<DeviceAxis, float>> values, int interval)
            => $"{values.Aggregate(string.Empty, (s, x) => $"{s} {ToString(x.Key, x.Value, interval)}")}\n".TrimStart();

        public static bool IsDirty(float value, float lastValue)
            => float.IsFinite(value) && (!float.IsFinite(lastValue) || MathF.Abs(lastValue - value) * 999 >= 1);
    }
}
