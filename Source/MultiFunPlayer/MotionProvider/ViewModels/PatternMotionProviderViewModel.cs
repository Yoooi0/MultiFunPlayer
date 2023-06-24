using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider.ViewModels;

internal enum PatternType
{
    Triangle,
    Sine,
    DoubleBounce,
    SharpBounce,
    Saw,
    Square
}

[DisplayName("Pattern")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal class PatternMotionProviderViewModel : AbstractMotionProvider
{
    private double _time;

    public PatternMotionProviderViewModel(DeviceAxis target, IEventAggregator eventAggregator)
        : base(target, eventAggregator) { }

    [JsonProperty] public PatternType Pattern { get; set; } = PatternType.Triangle;

    public override void Update(double deltaTime)
    {
        Value = MathUtils.Map(Calculate(Pattern, _time), 0, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }

    private double Calculate(PatternType pattern, double time)
    {
        var t = MathUtils.Clamp01(time % 4 / 4);
        switch (pattern)
        {
            case PatternType.Triangle: return Math.Abs(Math.Abs(t * 2 - 1.5) - 1);
            case PatternType.Sine: return -Math.Sin(t * Math.PI * 2) / 2 + 0.5;
            case PatternType.DoubleBounce:
                {
                    var x = t * Math.PI * 2 - Math.PI / 4;
                    return -(Math.Pow(Math.Sin(x), 5) + Math.Pow(Math.Cos(x), 5)) / 2 + 0.5;
                }
            case PatternType.SharpBounce:
                {
                    var x = (t + 0.41957) * Math.PI / 2;
                    var s = Math.Sin(x) * Math.Sin(x);
                    var c = Math.Cos(x) * Math.Cos(x);
                    return Math.Sqrt(Math.Max(c - s, s - c));
                }
            case PatternType.Saw: return t;
            case PatternType.Square: return t < 0.5 ? 1 : 0;
            default: return 0;
        }
    }

    public static void RegisterActions(IShortcutManager s, Func<DeviceAxis, PatternMotionProviderViewModel> getInstance)
    {
        void UpdateProperty(DeviceAxis axis, Action<PatternMotionProviderViewModel> callback)
        {
            var motionProvider = getInstance(axis);
            if (motionProvider != null)
                callback(motionProvider);
        }

        AbstractMotionProvider.RegisterActions(s, getInstance);
        var name = typeof(PatternMotionProviderViewModel).GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

        #region PatternMotionProvider::Pattern
        s.RegisterAction<DeviceAxis, PatternType>($"MotionProvider::{name}::Pattern::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Pattern").WithItemsSource(Enum.GetValues<PatternType>()),
            (axis, pattern) => UpdateProperty(axis, p => p.Pattern = pattern));
        #endregion
    }
}
