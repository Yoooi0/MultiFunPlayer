namespace MultiFunPlayer.MotionProvider;

public interface IMotionProvider
{
    string Name { get; }
    float Value { get; }

    float Speed { get; set; }
    float Minimum { get; set; }
    float Maximum { get; set; }

    void Update(float deltaTime);

    event EventHandler SyncRequest;
}
