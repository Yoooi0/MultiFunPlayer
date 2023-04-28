using MultiFunPlayer.Common;
using MultiFunPlayer.OutputTarget.ViewModels;
using Stylet;
using StyletIoC;

namespace MultiFunPlayer.OutputTarget;

internal interface IOutputTargetFactory
{
    IOutputTarget CreateOutputTarget(Type type, int index);
}

internal class OutputTargetFactory : IOutputTargetFactory
{
    private readonly IContainer _container;

    public OutputTargetFactory(IContainer container) => _container = container;

    public IOutputTarget CreateOutputTarget(Type type, int index)
    {
        if (index > MaxInstanceIndex(type))
            return null;

        var eventAggregator = _container.Get<IEventAggregator>();
        var valueProvider = _container.Get<IDeviceAxisValueProvider>();
        return (IOutputTarget)Activator.CreateInstance(type, new object[] { index, eventAggregator, valueProvider });
    }

    private int MaxInstanceIndex(Type type)
    {
        if (type == typeof(ButtplugOutputTargetViewModel))
            return 0;

        return 9;
    }
}