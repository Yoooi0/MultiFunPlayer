using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("WebSocket")]
public class WebSocketOutputTargetViewModel : AsyncAbstractOutputTarget
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public Uri Uri { get; set; } = new Uri("ws://127.0.0.1/ws");

    public WebSocketOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider) { }

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
            Logger.Warn(e, "Error when connecting to websocket");
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to websocket", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(WriteAsync(client, cancellationSource.Token), ReadAsync(client, cancellationSource.Token));
            cancellationSource.Cancel();

            if (task.Exception != null)
                throw task.Exception;
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
            var stopwatch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && client.State == WebSocketState.Open)
            {
                stopwatch.Restart();
                await Sleep(stopwatch, token);

                UpdateValues();

                var commands = DeviceAxis.ToString(Values, (float)stopwatch.Elapsed.TotalMilliseconds);
                if (client.State == WebSocketState.Open && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), Uri.ToString());
                    await client.SendAsync(Encoding.UTF8.GetBytes(commands), WebSocketMessageType.Text, true, token);
                }
            }
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
                } while(!token.IsCancellationRequested && !result.EndOfMessage);

                var message = Encoding.UTF8.GetString(memory.GetBuffer());
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
            if (Uri != null)
                settings[nameof(Uri)] = new JValue(Uri.ToString());
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<Uri>(nameof(Uri), out var uri))
                Uri = uri;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Uri
        s.RegisterAction($"{Identifier}::Uri::Set", b => b.WithSetting<string>(s => s.WithLabel("Uri").WithDescription("websocket uri")).WithCallback((_, uriString) =>
        {
            if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                Uri = uri;
        }));
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

            return await ValueTask.FromResult(true);
        }
        catch
        {
            return await ValueTask.FromResult(false);
        }
    }
}
