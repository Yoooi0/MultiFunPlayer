using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.VideoSource;
using MultiFunPlayer.ViewModels;
using Stylet;
using StyletIoC;

namespace MultiFunPlayer
{
    public class Bootstrapper : Bootstrapper<RootViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();
            builder.Bind<IVideoSource>().ToAllImplementations();
            builder.Bind<IOutputTarget>().ToAllImplementations();
        }
    }
}
