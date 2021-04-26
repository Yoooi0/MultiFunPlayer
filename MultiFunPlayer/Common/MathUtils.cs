using System;
using System.Runtime.CompilerServices;

namespace MultiFunPlayer.Common
{
    public static class MathUtils
    {
        private static readonly Random _random;

        static MathUtils()
        {
            _random = new Random();
        }

        public static float Clamp(float x, float from, float to)
                    => from <= to ? MathF.Max(MathF.Min(x, to), from) : MathF.Min(MathF.Max(x, to), from);

        public static float Clamp01(float x) => Clamp(x, 0, 1);
        public static float Lerp(float from, float to, float t) => Clamp(LerpUnclamped(from, to, t), from, to);
        public static float LerpUnclamped(float from, float to, float t) => from * (1 - t) + to * t;
        public static float UnLerp(float from, float to, float t) => Clamp01(UnLerpUnclamped(from, to, t));
        public static float UnLerpUnclamped(float from, float to, float t) => (t - from) / (to - from);
        public static float Map(float x, float from0, float to0, float from1, float to1) => Lerp(from1, to1, UnLerp(from0, to0, x));
        public static float MapUnclamped(float x, float from0, float to0, float from1, float to1) => LerpUnclamped(from1, to1, UnLerpUnclamped(from0, to0, x));
        public static void Swap<T>(ref T a, ref T b)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        public static int Random(int from, int to) => _random.Next(from, to);
        public static double Random() => _random.NextDouble();

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static float Pchip(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float x)
        {
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            static float Slope(float xkm, float ykm, float xk, float yk, float xkp, float ykp)
            {
                var hkm1 = xk - xkm;
                var dkm1 = (yk - ykm) / hkm1;

                var hk = xkp - xk;
                var dk = (ykp - yk) / hk;
                var w1 = 2 * hk + hkm1;
                var w2 = hk + 2 * hkm1;
                if ((dk > 0 && dkm1 < 0) || (dk < 0 && dkm1 > 0) || dk == 0 || dkm1 == 0)
                    return 0;

                return (w1 + w2) / (w1 / dkm1 + w2 / dk);
            }

            var s1 = Slope(x0, y0, x1, y1, x2, y2);
            var s2 = Slope(x1, y1, x2, y2, x3, y3);

            var d = x2 - x1;
            var dx = x - x1;
            var t = dx / d;
            var r = 1 - t;

            return r * r * (y1 * (1 + 2 * t) + dx * s1)
                 + t * t * (y2 * (3 - 2 * t) - d * s2 * r);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static float Makima(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float x)
        {
            var m2 = (y3 - y2) / (x3 - x2);
            var m1 = (y2 - y1) / (x2 - x1);
            var m0 = (y1 - y0) / (x1 - x0);

            var w11 = Math.Abs(m2 - m1) + Math.Abs(m2 + m1) / 2;
            var w12 = Math.Abs(m1 - m0) + Math.Abs(3 * m0 - m1) / 2;
            var s1 = (w11 * m0 + w12 * m1) / (w11 + w12);
            if (!double.IsFinite(s1))
                s1 = 0;

            var w21 = Math.Abs(m2 - m1) + Math.Abs(3 * m2 - m1) / 2;
            var w22 = Math.Abs(m1 - m0) + Math.Abs(m1 + m0) / 2;
            var s2 = (w21 * m1 + w22 * m2) / (w21 + w22);
            if (!double.IsFinite(s2))
                s2 = 0;

            var d = x2 - x1;
            var dx = x - x1;
            var t = dx / d;
            var r = 1 - t;

            return r * r * (y1 * (1 + 2 * t) + s1 * dx)
                 + t * t * (y2 * (3 - 2 * t) - d * s2 * r);
        }

        public static float Interpolate(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float x, InterpolationType type)
            => type switch
            {
                InterpolationType.Pchip => Pchip(x0, y0, x1, y1, x2, y2, x3, y3, x),
                InterpolationType.Makima => Makima(x0, y0, x1, y1, x2, y2, x3, y3, x),
                _ => throw new NotSupportedException()
            };
    }

    public enum InterpolationType
    {
        Pchip,
        Makima
    }

    public class OpenSimplex
    {
        private const int PSIZE = 2048;
        private const int PMASK = PSIZE - 1;

        private readonly static LatticePoint[] LatticeLookup = new LatticePoint[8 * 4];
        private readonly static Gradient[] GradLookup = new Gradient[PSIZE];

        private readonly short[] _perm;
        private readonly Gradient[] _grad;

        public OpenSimplex(long seed)
        {
            _perm = new short[PSIZE];
            _grad = new Gradient[PSIZE];
            var source = new short[PSIZE];

            for (var i = 0; i < PSIZE; i++)
                source[i] = (short) i;

            for (var i = PSIZE - 1; i >= 0; i--)
            {
                seed = seed * 6364136223846793005L + 1442695040888963407L;
                var r = (int)((seed + 31) % (i + 1));
                if (r < 0)
                    r += i + 1;

                _perm[i] = source[r];
                _grad[i] = GradLookup[_perm[i]];

                source[r] = source[i];
            }
        }

        static OpenSimplex()
        {
            for (var i = 0; i < 8; i++)
            {
                int i1, j1, i2, j2;
                if ((i & 1) == 0)
                {
                    if ((i & 2) == 0) { i1 = -1; j1 = 0; } else { i1 = 1; j1 = 0; }
                    if ((i & 4) == 0) { i2 = 0; j2 = -1; } else { i2 = 0; j2 = 1; }
                }
                else
                {
                    if ((i & 2) != 0) { i1 = 2; j1 = 1; } else { i1 = 0; j1 = 1; }
                    if ((i & 4) != 0) { i2 = 1; j2 = 2; } else { i2 = 1; j2 = 0; }
                }

                LatticeLookup[i * 4 + 0] = new LatticePoint(0, 0);
                LatticeLookup[i * 4 + 1] = new LatticePoint(1, 1);
                LatticeLookup[i * 4 + 2] = new LatticePoint(i1, j1);
                LatticeLookup[i * 4 + 3] = new LatticePoint(i2, j2);
            }

            const double n = 0.05481866495625118;
            var grad = new Gradient[]{
                new( 0.130526192220052 / n,  0.991444861373810 / n),
                new( 0.382683432365090 / n,  0.923879532511287 / n),
                new( 0.608761429008721 / n,  0.793353340291235 / n),
                new( 0.793353340291235 / n,  0.608761429008721 / n),
                new( 0.923879532511287 / n,  0.382683432365090 / n),
                new( 0.991444861373810 / n,  0.130526192220051 / n),
                new( 0.991444861373810 / n, -0.130526192220051 / n),
                new( 0.923879532511287 / n, -0.382683432365090 / n),
                new( 0.793353340291235 / n, -0.608761429008720 / n),
                new( 0.608761429008721 / n, -0.793353340291235 / n),
                new( 0.382683432365090 / n, -0.923879532511287 / n),
                new( 0.130526192220052 / n, -0.991444861373810 / n),
                new(-0.130526192220052 / n, -0.991444861373810 / n),
                new(-0.382683432365090 / n, -0.923879532511287 / n),
                new(-0.608761429008721 / n, -0.793353340291235 / n),
                new(-0.793353340291235 / n, -0.608761429008721 / n),
                new(-0.923879532511287 / n, -0.382683432365090 / n),
                new(-0.991444861373810 / n, -0.130526192220052 / n),
                new(-0.991444861373810 / n,  0.130526192220051 / n),
                new(-0.923879532511287 / n,  0.382683432365090 / n),
                new(-0.793353340291235 / n,  0.608761429008721 / n),
                new(-0.608761429008721 / n,  0.793353340291235 / n),
                new(-0.382683432365090 / n,  0.923879532511287 / n),
                new(-0.130526192220052 / n,  0.991444861373810 / n)
            };

            for (var i = 0; i < PSIZE; i++)
                GradLookup[i] = grad[i % grad.Length];
        }

        public double Calculate2D(double x, double y)
        {
            var s = 0.366025403784439 * (x + y);
            return Calculate2DImpl(x + s, y + s);
        }

        private double Calculate2DImpl(double xs, double ys)
        {
            var value = 0.0;
            var xsb = FastFloor(xs);
            var ysb = FastFloor(ys);
            var xsi = xs - xsb;
            var ysi = ys - ysb;

            var a = (int)(xsi + ysi);
            var index =
                (a << 2) |
                (int)(xsi - ysi / 2 + 1 - a / 2.0) << 3 |
                (int)(ysi - xsi / 2 + 1 - a / 2.0) << 4;

            var ssi = (xsi + ysi) * -0.211324865405187;
            var xi = xsi + ssi;
            var yi = ysi + ssi;

            for (var i = 0; i < 4; i++)
            {
                var c = LatticeLookup[index + i];

                var dx = xi + c.dx;
                var dy = yi + c.dy;
                var attn = 2.0 / 3.0 - dx * dx - dy * dy;
                if (attn <= 0)
                    continue;

                var pxm = (xsb + c.xsv) & PMASK;
                var pym = (ysb + c.ysv) & PMASK;
                var grad = _grad[_perm[pxm] ^ pym];
                var extrapolation = grad.dx * dx + grad.dy * dy;

                attn *= attn;
                value += attn * attn * extrapolation;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastFloor(double x)
        {
            var xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }

        private struct LatticePoint
        {
            public int xsv, ysv;
            public double dx, dy;

            public LatticePoint(int xsv, int ysv)
            {
                var ssv = (xsv + ysv) * -0.211324865405187;

                this.xsv = xsv; this.ysv = ysv;
                this.dx = -xsv - ssv;
                this.dy = -ysv - ssv;
            }
        }

        private struct Gradient
        {
            public double dx, dy;

            public Gradient(double dx, double dy)
            {
                this.dx = dx; this.dy = dy;
            }
        }
    }
}
