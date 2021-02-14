using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public abstract class AbstractVideoSource : PropertyChangedBase, IVideoSource, IHandle<AppSettingsMessage>
    {
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        public VideoSourceStatus Status { get; protected set; }
        public virtual object SettingsViewModel { get; }

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

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("VideoSource")
                 || !message.Settings.TryGetObject(out var settings, "VideoSource"))
                    return;

                if(SettingsViewModel != null)
                    settings[Name] = JObject.FromObject(SettingsViewModel);
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "VideoSource"))
                    return;

                if(settings.TryGetObject(out var videoSourceSettings, Name))
                    videoSourceSettings.Populate(SettingsViewModel);
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
