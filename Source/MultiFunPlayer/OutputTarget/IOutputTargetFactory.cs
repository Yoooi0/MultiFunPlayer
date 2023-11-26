using MultiFunPlayer.OutputTarget.ViewModels;
using StyletIoC;

namespace MultiFunPlayer.OutputTarget;

internal interface IOutputTargetFactory
{
    IOutputTarget CreateOutputTarget(Type type, int index);
}

internal sealed class OutputTargetFactory(IContainer container) : IOutputTargetFactory
{
    public IOutputTarget CreateOutputTarget(Type type, int index)
    {
        if (index > MaxInstanceIndex(type))
            return null;

        var arguments = type.GetConstructors()[0]
                            .GetParameters()
                            .Skip(1)
                            .Select(p => container.GetTypeOrAll(p.ParameterType))
                            .Prepend(index)
                            .ToArray();

        return (IOutputTarget)Activator.CreateInstance(type, arguments);
    }

    private int MaxInstanceIndex(Type type)
    {
        if (type == typeof(ButtplugOutputTargetViewModel))
            return 0;

        return 9;
    }
}