using System;

namespace MultiFunPlayer.Common
{
    public static class MathUtils
    {
        public static float Clamp(float x, float from, float to)
                    => from <= to ? Math.Max(Math.Min(x, to), from) : Math.Min(Math.Max(x, to), from);
        public static int Clamp(int x, int from, int to)
                    => from <= to ? Math.Max(Math.Min(x, to), from) : Math.Min(Math.Max(x, to), from);

        public static float Clamp01(float x) => Clamp(x, 0, 1);
        public static float Lerp(float from, float to, float t) => Clamp(LerpUnclamped(from, to, t), from, to);
        public static float LerpUnclamped(float from, float to, float t) => from * (1 - t) + to * t;
        public static float UnLerp(float from, float to, float t) => Clamp01(UnLerpUnclamped(from, to, t));
        public static float UnLerpUnclamped(float from, float to, float t) => (t - from) / (to - from);
        public static float Map(float x, float from0, float to0, float from1, float to1) => Lerp(from1, to1, UnLerp(from0, to0, x));
        public static float MapUnclamped(float x, float from0, float to0, float from1, float to1) => LerpUnclamped(from1, to1, UnLerpUnclamped(from0, to0, x));
        public static void Swap<T>(ref T a, ref T b)
        {
            T temp = a;
            a = b;
            b = temp;
        }

    }
}
