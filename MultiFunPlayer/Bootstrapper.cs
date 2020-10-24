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
            builder.Bind<ValuesViewModel>().ToSelf().InSingletonScope();
            //builder.Bind<IDeviceAxisValueProvider>().To<ValuesViewModel>().InSingletonScope(); //TODO:
            builder.Bind<IVideoPlayer>().ToAllImplementations();
        }
    }
}
