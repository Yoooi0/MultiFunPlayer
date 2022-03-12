using MultiFunPlayer.Common;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.MotionProvider;

public interface IMotionProviderFactory
{
    IMotionProvider CreateMotionProvider(Type type, DeviceAxis target);
    IEnumerable<IMotionProvider> CreateMotionProviderCollection(DeviceAxis target);
}

public class MotionProviderFactory : IMotionProviderFactory
{
    private readonly IEventAggregator _eventAggregator;

    public MotionProviderFactory(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }

    public IMotionProvider CreateMotionProvider(Type type, DeviceAxis target)
        => (IMotionProvider) Activator.CreateInstance(type, new object[] { target, _eventAggregator });

    public IEnumerable<IMotionProvider> CreateMotionProviderCollection(DeviceAxis target)
        => ReflectionUtils.FindImplementations<IMotionProvider>().Select(t => CreateMotionProvider(t, target));
}