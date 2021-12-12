using MultiFunPlayer.Common;
using Newtonsoft.Json;
using System.ComponentModel;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Random")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class RandomMotionProviderViewModel : AbstractMotionProvider
{
    private readonly OpenSimplex _noise;
    private float _time;

    [JsonProperty] public int Octaves { get; set; } = 1;
    [JsonProperty] public float Persistence { get; set; } = 1;
    [JsonProperty] public float Lacunarity { get; set; } = 1;

    public RandomMotionProviderViewModel()
    {
        _noise = new OpenSimplex(Random.Shared.NextInt64());
    }

    public override void Update(float deltaTime)
    {
        var noise = (float)_noise.Calculate2D(_time, _time, Octaves, Persistence, Lacunarity);
        Value = MathUtils.Map(noise, -1, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }
}
