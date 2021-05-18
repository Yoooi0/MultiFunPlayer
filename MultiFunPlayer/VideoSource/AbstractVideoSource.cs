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
        private readonly AsyncManualResetEvent _statusEvent;
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        [SuppressPropertyChangedWarnings] public abstract VideoSourceStatus Status { get; protected set; }
        public bool ContentVisible { get; set; } = false;
        public bool AutoConnectEnabled { get; set; } = true;

        protected AbstractVideoSource(IEventAggregator eventAggregator)
        {
            _statusEvent = new AsyncManualResetEvent();

            eventAggregator.Subscribe(this);
            PropertyChanged += (s, e) =>
            {
                if (string.Equals(e.PropertyName, "Status", StringComparison.OrdinalIgnoreCase))
                    _statusEvent.Reset();
            };
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

            await Task.CompletedTask;
        }

        public async virtual Task DisconnectAsync()
        {
            if (Status == VideoSourceStatus.Disconnected || Status == VideoSourceStatus.Disconnecting)
                return;

            Status = VideoSourceStatus.Disconnecting;

            _cancellationSource?.Cancel();

            if (_task != null)
                await _task;

            await Task.Delay(250);
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;

            Status = VideoSourceStatus.Disconnected;
        }

        public async virtual ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(false);
        public async virtual ValueTask<bool> CanConnectAsyncWithStatus(CancellationToken token)
        {
            if (Status != VideoSourceStatus.Disconnected)
                return await ValueTask.FromResult(false);

            Status = VideoSourceStatus.Connecting;
            await Task.Delay(100, token);
            var result = await CanConnectAsync(token);
            Status = VideoSourceStatus.Disconnected;

            return result;
        }

        public async Task WaitForStatus(IEnumerable<VideoSourceStatus> statuses, CancellationToken token)
        {
            while (!statuses.Contains(Status))
                await _statusEvent.WaitAsync(token);
        }

        protected abstract void HandleSettings(JObject settings, AppSettingsMessageType type);
        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("VideoSource", Name)
                 || !message.Settings.TryGetObject(out var settings, "VideoSource", Name))
                    return;

                settings[nameof(AutoConnectEnabled)] = new JValue(AutoConnectEnabled);

                HandleSettings(settings, message.Type);
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "VideoSource", Name))
                    return;

                if (settings.TryGetValue<bool>(nameof(AutoConnectEnabled), out var autoConnectEnabled))
                    AutoConnectEnabled = autoConnectEnabled;

                HandleSettings(settings, message.Type);
            }
        }

        protected async virtual void Dispose(bool disposing)
        {
            await DisconnectAsync();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
