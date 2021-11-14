using MultiFunPlayer.Common;
using Newtonsoft.Json;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class RandomMotionProviderViewModel : AbstractMotionProvider
{
    private readonly OpenSimplex _noise;
    private float _time;
    private long _lastTime;

    public override string Name => "Random";

    [JsonProperty] public float Speed { get; set; } = 1;
    [JsonProperty] public float Minimum { get; set; } = 0;
    [JsonProperty] public float Maximum { get; set; } = 100;
    [JsonProperty] public int Seed { get; set; } = 0;

    public RandomMotionProviderViewModel()
    {
        _noise = new OpenSimplex(0);
        _lastTime = Environment.TickCount64;
        _time = 0;
    }

    public override void Update()
    {
        var currentTime = Environment.TickCount64;
        Value = MathUtils.Map((float)(_noise?.Calculate2D(Seed, _time) + 1 ?? 0) / 2, 0, 1, Minimum / 100, Maximum / 100);

        _time += Speed * (currentTime - _lastTime) / 1000.0f;
        _lastTime = currentTime;
    }
}
