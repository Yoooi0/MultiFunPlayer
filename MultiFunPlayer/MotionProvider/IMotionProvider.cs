namespace MultiFunPlayer.MotionProvider;

public interface IMotionProvider
{
    string Name { get; }
    double Value { get; }

    double Speed { get; set; }
    double Minimum { get; set; }
    double Maximum { get; set; }

    void Update(double deltaTime);
}
