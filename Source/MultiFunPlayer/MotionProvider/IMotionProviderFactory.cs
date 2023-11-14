using MultiFunPlayer.Common;
using Stylet;
using StyletIoC;

namespace MultiFunPlayer.MotionProvider;

internal interface IMotionProviderFactory
{
    IMotionProvider CreateMotionProvider(Type type, DeviceAxis target);
    IEnumerable<IMotionProvider> CreateMotionProviderCollection(DeviceAxis target);
}

internal class MotionProviderFactory(IContainer container) : IMotionProviderFactory
{
    public IMotionProvider CreateMotionProvider(Type type, DeviceAxis target)
        => (IMotionProvider)Activator.CreateInstance(type, new object[] { target, container.Get<IEventAggregator>() });

    public IEnumerable<IMotionProvider> CreateMotionProviderCollection(DeviceAxis target)
        => ReflectionUtils.FindImplementations<IMotionProvider>().Select(t => CreateMotionProvider(t, target));
}