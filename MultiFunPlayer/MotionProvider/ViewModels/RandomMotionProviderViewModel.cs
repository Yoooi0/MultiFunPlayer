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

    [JsonProperty] public int Seed { get; set; } = 0;

    public RandomMotionProviderViewModel()
    {
        _noise = new OpenSimplex(0);
    }

    public override void Update(float deltaTime)
    {
        Value = MathUtils.Map((float)(_noise?.Calculate2D(Seed, _time) + 1 ?? 0) / 2, 0, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }
}
