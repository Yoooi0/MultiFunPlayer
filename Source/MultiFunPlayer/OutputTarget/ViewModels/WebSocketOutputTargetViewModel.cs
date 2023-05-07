using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.Net.WebSockets;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("WebSocket")]
internal class WebSocketOutputTargetViewModel : AsyncAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public bool OffloadElapsedTime { get; set; } = true;
    public bool SendDirtyValuesOnly { get; set; } = true;
    public Uri Uri { get; set; } = new Uri("ws://127.0.0.1/ws");

    public WebSocketOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        UpdateInterval = 16;
    }

    public override int MinimumUpdateInterval => 16;
    public override int MaximumUpdateInterval => 200;

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        using var client = new ClientWebSocket();

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, Uri.ToString());
            await client.ConnectAsync(Uri, token)
                        .WithCancellation(1000);
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error when connecting to websocket");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to websocket", "RootDialog");
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
    }

    private async Task WriteAsync(ClientWebSocket client, CancellationToken token)
    {
        try
        {
            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            await FixedUpdateAsync(() => !token.IsCancellationRequested && client.State == WebSocketState.Open, async elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                var values = SendDirtyValuesOnly ? Values.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : Values;
                values = values.Where(x => AxisSettings[x.Key].Enabled);

                var commands = OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                if (client.State == WebSocketState.Open && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), Uri.ToString());
                    await client.SendAsync(Encoding.UTF8.GetBytes(commands), WebSocketMessageType.Text, true, token);
                    lastSentValues.Merge(values);
                }
            }, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReadAsync(ClientWebSocket client, CancellationToken token)
    {
        var readBuffer = new byte[1024];

        try
        {
            while (!token.IsCancellationRequested)
            {
                using var memory = new MemoryStream();

                var result = default(WebSocketReceiveResult);
                do
                {
                    result = await client.ReceiveAsync(readBuffer, token);
                    memory.Write(readBuffer, 0, result.Count);
                } while (!token.IsCancellationRequested && !result.EndOfMessage);

                var message = Encoding.UTF8.GetString(memory.ToArray());
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
            settings[nameof(Uri)] = Uri?.ToString();
            settings[nameof(OffloadElapsedTime)] = OffloadElapsedTime;
            settings[nameof(SendDirtyValuesOnly)] = SendDirtyValuesOnly;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<Uri>(nameof(Uri), out var uri))
                Uri = uri;
            if (settings.TryGetValue<bool>(nameof(OffloadElapsedTime), out var offloadElapsedTime))
                OffloadElapsedTime = offloadElapsedTime;
            if (settings.TryGetValue<bool>(nameof(SendDirtyValuesOnly), out var sendDirtyValuesOnly))
                SendDirtyValuesOnly = sendDirtyValuesOnly;
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

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            using var client = new ClientWebSocket();
            await client.ConnectAsync(Uri, token)
                        .WithCancellation(250);

            await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
