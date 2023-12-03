using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Random")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal sealed class RandomMotionProvider : AbstractMotionProvider
{
    private readonly OpenSimplex _noise;
    private double _time;

    [JsonProperty] public int Octaves { get; set; } = 1;
    [JsonProperty] public double Persistence { get; set; } = 1;
    [JsonProperty] public double Lacunarity { get; set; } = 1;

    public RandomMotionProvider(DeviceAxis target, IEventAggregator eventAggregator)
        : base(target, eventAggregator)
    {
        _noise = new OpenSimplex(Random.Shared.NextInt64());
        Speed = 0.3;
    }

    public override void Update(double deltaTime)
    {
        var noise = _noise.Calculate2D(_time, _time, Octaves, Persistence, Lacunarity);
        Value = MathUtils.Map(noise, -1, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }

    public static void RegisterActions(IShortcutManager s, Func<DeviceAxis, RandomMotionProvider> getInstance)
    {
        void UpdateProperty(DeviceAxis axis, Action<RandomMotionProvider> callback)
        {
            var motionProvider = getInstance(axis);
            if (motionProvider != null)
                callback(motionProvider);
        }

        AbstractMotionProvider.RegisterActions(s, getInstance);
        var name = typeof(RandomMotionProvider).GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

        #region RandomMotionProvider::Octaves
        s.RegisterAction<DeviceAxis, int>($"MotionProvider::{name}::Octaves::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Octaves"),
            (axis, octaves) => UpdateProperty(axis, p => p.Octaves = octaves));
        #endregion

        #region RandomMotionProvider::Persistence
        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Persistence::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Persistence"),
            (axis, persistence) => UpdateProperty(axis, p => p.Persistence = persistence));
        #endregion

        #region RandomMotionProvider::Lacunarity
        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Lacunarity::Set",
           s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
           s => s.WithLabel("Lacunarity"),
           (axis, lacunarity) => UpdateProperty(axis, p => p.Lacunarity = lacunarity));
        #endregion
    }
}
