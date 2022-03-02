using MultiFunPlayer.OutputTarget;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public interface IOutputTargetFactory
{
    IOutputTarget CreateOutputTarget(Type type, int index);
}

public class OutputTargetFactory : IOutputTargetFactory
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IDeviceAxisValueProvider _valueProvider;

    public OutputTargetFactory(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    {
        _eventAggregator = eventAggregator;
        _valueProvider = valueProvider;
    }

    public IOutputTarget CreateOutputTarget(Type type, int index)
        => (IOutputTarget)Activator.CreateInstance(type, new object[] { _eventAggregator, _valueProvider });
}