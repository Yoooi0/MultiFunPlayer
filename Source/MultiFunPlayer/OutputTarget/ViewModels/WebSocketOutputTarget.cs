using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("WebSocket")]
internal sealed class WebSocketOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : AsyncAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public DeviceAxisUpdateType UpdateType { get; set; } = DeviceAxisUpdateType.FixedUpdate;
    public bool CanChangeUpdateType => !IsConnectBusy && !IsConnected;

    public Uri Uri { get; set; } = new Uri("ws://127.0.0.1/ws");

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new TCodeAsyncFixedUpdateContext() { UpdateInterval = 16, MinimumUpdateInterval = 16, MaximumUpdateInterval = 200 },
        DeviceAxisUpdateType.PolledUpdate => new AsyncPolledUpdateContext(),
        _ => null,
    };

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Identifier, Uri, connectionType);

        if (Uri == null)
            throw new OutputTargetException("Uri cannot be null");

        return ValueTask.FromResult(true);
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        using var client = new ClientWebSocket();

        try
        {
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancellationSource.CancelAfter(500);
            await client.ConnectAsync(Uri, cancellationSource.Token);

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0} at \"{1}\"", Name, Uri);
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to {Name}", "RootDialog");
            return;
        }
        catch
        {
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(WriteAsync(client, cancellationSource.Token), ReadAsync(client, cancellationSource.Token));
            cancellationSource.Cancel();

            task.ThrowIfFaulted();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }

        try { await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
        catch { }
    }

    private async Task WriteAsync(ClientWebSocket client, CancellationToken token)
    {
        try
        {
            var buffer = new byte[256];
            if (UpdateType == DeviceAxisUpdateType.FixedUpdate)
            {
                var currentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
                var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
                await FixedUpdateAsync<TCodeAsyncFixedUpdateContext>(() => !token.IsCancellationRequested && client.State == WebSocketState.Open, async (context, elapsed) =>
                {
                    Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                    GetValues(currentValues);

                    var values = context.SendDirtyValuesOnly ? currentValues.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : currentValues;
                    values = values.Where(x => AxisSettings[x.Key].Enabled);

                    var commands = context.OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                    if (client.State == WebSocketState.Open && !string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), Uri.ToString());

                        var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                        await client.SendAsync(buffer.AsMemory(0, encoded), WebSocketMessageType.Text, true, token);
                        lastSentValues.Merge(values);
                    }
                }, token);
            }
            else if (UpdateType == DeviceAxisUpdateType.PolledUpdate)
            {
                await PolledUpdateAsync(DeviceAxis.All, () => !token.IsCancellationRequested, async (_, axis, snapshot, elapsed) =>
                {
                    Logger.Trace("Begin PolledUpdate [Axis: {0}, Index From: {1}, Index To: {2}, Duration: {3}, Elapsed: {4}]",
                        axis, snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);

                    var settings = AxisSettings[axis];
                    if (!settings.Enabled)
                        return;
                    if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                        return;

                    var value = MathUtils.Lerp(settings.Minimum, settings.Maximum, snapshot.KeyframeTo.Value);
                    var duration = snapshot.Duration;

                    var command = DeviceAxis.ToString(axis, value, duration * 1000);
                    if (client.State == WebSocketState.Open && !string.IsNullOrWhiteSpace(command))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", command, Uri.ToString());

                        var encoded = Encoding.UTF8.GetBytes($"{command}\n", buffer);
                        await client.SendAsync(buffer.AsMemory(0, encoded), WebSocketMessageType.Text, true, token);
                    }
                }, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReadAsync(ClientWebSocket client, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var message = Encoding.UTF8.GetString(await client.ReceiveAsync(token));
                Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);
            }
        }
        catch (OperationCanceledException) { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(UpdateType)] = JToken.FromObject(UpdateType);
            settings[nameof(Uri)] = Uri?.ToString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DeviceAxisUpdateType>(nameof(UpdateType), out var updateType))
                UpdateType = updateType;
            if (settings.TryGetValue<Uri>(nameof(Uri), out var uri))
                Uri = uri;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Uri
        s.RegisterAction<string>($"{Identifier}::Uri::Set", s => s.WithLabel("Uri").WithDescription("websocket uri"), uriString =>
        {
            if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                Uri = uri;
        });
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::Uri::Set");
    }
}
