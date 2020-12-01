using MultiFunPlayer.Common;
using MultiFunPlayer.Player;
using MultiFunPlayer.ViewModels;
using Stylet;
using StyletIoC;

namespace MultiFunPlayer
{
    public class Bootstrapper : Bootstrapper<RootViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            builder.Bind<ScriptViewModel>().ToSelf().InSingletonScope();
            builder.Bind<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();
            builder.Bind<IVideoPlayer>().ToAllImplementations();
        }
    }
}
