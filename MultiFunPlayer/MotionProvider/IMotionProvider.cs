namespace MultiFunPlayer.MotionProvider;

public interface IMotionProvider
{
    string Name { get; }
    float Value { get; }

    void Update();
}
