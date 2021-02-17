using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public abstract class AbstractVideoSource : Screen, IVideoSource, IHandle<AppSettingsMessage>
    {
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        [SuppressPropertyChangedWarnings] public abstract VideoSourceStatus Status { get; protected set; }
        public bool ContentVisible { get; set; }

        protected AbstractVideoSource(IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);
        }

        public abstract string Name { get; }
        protected abstract Task RunAsync(CancellationToken token);

        public async virtual Task ConnectAsync()
        {
            if (Status != VideoSourceStatus.Disconnected)
                return;

            Status = VideoSourceStatus.Connecting;
            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => DisconnectAsync()).Unwrap();

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async virtual Task DisconnectAsync()
        {
            if (Status == VideoSourceStatus.Disconnected || Status == VideoSourceStatus.Disconnecting)
                return;

            Status = VideoSourceStatus.Disconnecting;

            _cancellationSource?.Cancel();

            if (_task != null)
                await _task.ConfigureAwait(false);

            await Task.Delay(250).ConfigureAwait(false);
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;

            Status = VideoSourceStatus.Disconnected;
        }

        public async virtual ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(false).ConfigureAwait(false);
        public async Task WaitForStatus(IEnumerable<VideoSourceStatus> statuses, int checkFrequency, CancellationToken token)
        {
            if (statuses.Contains(Status))
                return;

            //TODO: not great, not terrible
            while (!statuses.Contains(Status))
                await Task.Delay(checkFrequency, token).ConfigureAwait(false);
        }

        protected abstract void HandleSettings(JObject settings, AppSettingsMessageType type);
        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("VideoSource", Name)
                 || !message.Settings.TryGetObject(out var settings, "VideoSource", Name))
                    return;

                HandleSettings(settings, message.Type);
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "VideoSource", Name))
                    return;

                HandleSettings(settings, message.Type);
            }
        }

        protected async virtual void Dispose(bool disposing)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
