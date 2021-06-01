using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.VideoSource;
using MultiFunPlayer.ViewModels;
using NLog;
using Stylet;
using StyletIoC;
using System;

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

        protected override void OnStart()
        {
            var logger = LogManager.GetLogger(nameof(AppDomain));
            AppDomain.CurrentDomain.UnhandledException += (s, e) => logger.Fatal(e.ExceptionObject as Exception);
        }
    }
}
