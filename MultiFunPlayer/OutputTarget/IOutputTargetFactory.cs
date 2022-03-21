using MultiFunPlayer.Common;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.OutputTarget.ViewModels;
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
    {
        if (index > MaxInstanceIndex(type))
            return null;

        return (IOutputTarget)Activator.CreateInstance(type, new object[] { index, _eventAggregator, _valueProvider });
    }

    private int MaxInstanceIndex(Type type)
    {
        if (type == typeof(ButtplugOutputTargetViewModel))
            return 0;

        return 9;
    }
}