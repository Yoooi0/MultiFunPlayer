using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Random")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class RandomMotionProviderViewModel : AbstractMotionProvider
{
    private readonly OpenSimplex _noise;
    private double _time;

    [JsonProperty] public int Octaves { get; set; } = 1;
    [JsonProperty] public double Persistence { get; set; } = 1;
    [JsonProperty] public double Lacunarity { get; set; } = 1;

    public RandomMotionProviderViewModel(DeviceAxis target, IEventAggregator eventAggregator)
        : base(target, eventAggregator)
    {
        _noise = new OpenSimplex(Random.Shared.NextInt64());
    }

    public override void Update(double deltaTime)
    {
        var noise = _noise.Calculate2D(_time, _time, Octaves, Persistence, Lacunarity);
        Value = MathUtils.Map(noise, -1, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }
}
