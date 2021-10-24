using Newtonsoft.Json;
using System;

namespace MultiFunPlayer.MotionProvider.ViewModels
{
    public enum PatternType
    {
        Triangle,
        Sine,
        DoubleBounce,
        SharpBounce,
        Saw,
        Square
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class PatternMotionProviderViewModel : AbstractMotionProvider
    {
        private float _time;
        private long _lastTime;

        public override string Name => "Pattern";

        [JsonProperty] public float Speed { get; set; } = 1;
        [JsonProperty] public PatternType Pattern { get; set; } = PatternType.Triangle;

        public PatternMotionProviderViewModel()
        {
            _lastTime = Environment.TickCount64;
            _time = 0;
        }

        public override void Update()
        {
            var currentTime = Environment.TickCount64;
            Value = Calculate(Pattern, _time);

            _time += Speed * (currentTime - _lastTime) / 1000.0f;
            _lastTime = currentTime;
        }

        private float Calculate(PatternType pattern, float time)
        {
            var t = time % 1;
            switch (pattern)
            {
                case PatternType.Triangle: return MathF.Abs(MathF.Abs(t * 2 - 1.5f) - 1);
                case PatternType.Sine: return -MathF.Sin(t * MathF.PI * 2) / 2 + 0.5f;
                case PatternType.DoubleBounce:
                    {
                        var x = t * MathF.PI * 2 - MathF.PI / 4;
                        return -(MathF.Pow(MathF.Sin(x), 5) + MathF.Pow(MathF.Cos(x), 5)) / 2 + 0.5f;
                    }
                case PatternType.SharpBounce:
                    {
                        var x = (t + 0.41957f) * MathF.PI / 2;
                        var s = MathF.Sin(x) * MathF.Sin(x);
                        var c = MathF.Cos(x) * MathF.Cos(x);
                        return MathF.Sqrt(MathF.Max(c - s, s - c));
                    }
                case PatternType.Saw: return t;
                case PatternType.Square: return t < 0.5f ? 1 : 0;
                default: return 0;
            }
        }
    }
}
