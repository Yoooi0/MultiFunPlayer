namespace MultiFunPlayer.MotionProvider;

internal interface IMotionProvider
{
    string Name { get; }
    double Value { get; }

    void Update(double deltaTime);
}
