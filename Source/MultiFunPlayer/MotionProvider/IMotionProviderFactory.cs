using MultiFunPlayer.Common;
using Stylet;
using StyletIoC;

namespace MultiFunPlayer.MotionProvider;

internal interface IMotionProviderFactory
{
    IMotionProvider CreateMotionProvider(Type type, DeviceAxis target);
    IEnumerable<IMotionProvider> CreateMotionProviderCollection(DeviceAxis target);
}

internal class MotionProviderFactory : IMotionProviderFactory
{
    private readonly IContainer _container;

    public MotionProviderFactory(IContainer container) => _container = container;

    public IMotionProvider CreateMotionProvider(Type type, DeviceAxis target)
        => (IMotionProvider)Activator.CreateInstance(type, new object[] { target, _container.Get<IEventAggregator>() });

    public IEnumerable<IMotionProvider> CreateMotionProviderCollection(DeviceAxis target)
        => ReflectionUtils.FindImplementations<IMotionProvider>().Select(t => CreateMotionProvider(t, target));
}