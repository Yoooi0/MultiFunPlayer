using MultiFunPlayer.Common.Messages;
using Stylet;
using System;

namespace MultiFunPlayer.OutputTarget
{
    public abstract class AbstractOutputTarget : Screen, IHandle<AppSettingsMessage>, IDisposable, IOutputTarget
    {
        public OutputTargetStatus Status { get; protected set; }

        public abstract string Name { get; }
        public abstract void Handle(AppSettingsMessage message);

        protected IDeviceAxisValueProvider ValueProvider { get; }

        protected AbstractOutputTarget(IDeviceAxisValueProvider valueProvider)
            => ValueProvider = valueProvider;

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}