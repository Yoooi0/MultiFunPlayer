using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("The Handy")]
internal sealed class TheHandyOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : AsyncAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    public string ConnectionKey { get; set; } = null;
    public DeviceAxis SourceAxis { get; set; } = null;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SourceAxis != null;

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.PolledUpdate => new AsyncPolledUpdateContext(),
        _ => null,
    };

    public void OnSourceAxisChanged()
    {
        if (Status != ConnectionStatus.Connected || SourceAxis == null)
            return;

        EventAggregator.Publish(new SyncRequestMessage(SourceAxis));
    }

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Identifier, ConnectionKey, connectionType);

        if (string.IsNullOrWhiteSpace(ConnectionKey))
            throw new OutputTargetException("Invalid connection key");
        if (SourceAxis == null)
            throw new OutputTargetException("Source axis not selected");

        return ValueTask.FromResult(true);
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        using var client = NetUtils.CreateHttpClient();

        try
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Connection-Key", ConnectionKey);

            {
                var response = await ApiGetAsync(client, "connected", token);
                if (!response.TryGetValue<bool>("connected", out var connected) || !connected)
                    throw new OutputTargetException("Device is not connected");
            }

            {
                var response = await ApiGetAsync(client, "info", token);
                if (!response.TryGetValue<int>("fwStatus", out var firmwareStatus) || firmwareStatus == 1)
                    throw new OutputTargetException("Out of date firmware version, update required");
            }

            {
                var response = await ApiPutAsync(client, "mode", "{ \"mode\": 2 }", token);
                if (!response.TryGetValue<int>("result", out var result) || result == -1)
                    throw new OutputTargetException($"Unable to set HDSP device mode [Response: {response.ToString(Formatting.None)}]");
            }
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0}", Name);
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to {Name}", "RootDialog");
            return;
        }
        catch
        {
            return;
        }

        try
        {
            const int sampleCount = 30;
            const int discardCount = 3;
            var samples = new List<long>(sampleCount);

            Logger.Debug("Calculating RTD time");
            for(var i = 0; i < sampleCount; i++)
            {
                var sendTicks = Stopwatch.GetTimestamp();
                var response = await ApiGetAsync(client, "servertime", token);
                var sample = Stopwatch.GetTimestamp() - sendTicks;

                Logger.Debug("RTD sample: {0}", sample);
                samples.Add(sample);
            }

            samples.Sort();
            var averageRtd = samples.Take(discardCount..(sampleCount-discardCount)).Average() / Stopwatch.Frequency;
            Logger.Info("Calculated RTD: {0}", averageRtd);

            Status = ConnectionStatus.Connected;
            EventAggregator.Publish(new SyncRequestMessage());

            await PolledUpdateAsync(SourceAxis, () => !token.IsCancellationRequested, async (_, snapshot, elapsed) =>
            {
                Logger.Trace("Begin PolledUpdate [Index From: {0}, Index To: {1}, Duration: {2}, Elapsed: {3}]", snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);
                if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                    return;

                if (!AxisSettings[SourceAxis].Enabled)
                    return;

                var position = Math.Clamp(snapshot.KeyframeTo.Value * 100, 0, 100);
                var duration = (int)Math.Floor(snapshot.Duration * 1000 + 0.75);
                var content = $"{{ \"immediateResponse\": true, \"stopOnTarget\": true, \"duration\": {duration}, \"position\": {position.ToString(CultureInfo.InvariantCulture)} }}";

                var result = await ApiPutAsync(client, "hdsp/xpt", content, token);
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }
    }

    private static Uri ApiUri(string path) => new($"https://www.handyfeeling.com/api/handy/v2/{path}");
    private async Task<JObject> ApiReadResponseAsync(HttpResponseMessage message, CancellationToken token)
    {
        var response = JObject.Parse(await message.Content.ReadAsStringAsync(token));

        Logger.Trace("{0} api response [Content: {1}]", Identifier, response.ToString(Formatting.None));
        if (response.TryGetObject(out var error, "error"))
            throw new OutputTargetException($"Api call failed: {error.ToString(Formatting.None)}");

        return response;
    }

    private async Task<JObject> ApiGetAsync(HttpClient client, string path, CancellationToken token)
    {
        var uri = ApiUri(path);
        Logger.Trace("{0} api get [URI: {1}]", Identifier, uri);

        var result = await client.GetAsync(uri, token);
        result.EnsureSuccessStatusCode();
        return await ApiReadResponseAsync(result, token);
    }

    private async Task<JObject> ApiPutAsync(HttpClient client, string path, string content, CancellationToken token)
    {
        var uri = ApiUri(path);
        Logger.Trace("{0} api put [URI: {1}, Content: {2}]", Identifier, uri, content);

        var result = await client.PutAsync(ApiUri(path), new StringContent(content, Encoding.UTF8, "application/json"), token);
        result.EnsureSuccessStatusCode();
        return await ApiReadResponseAsync(result, token);
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(SourceAxis)] = SourceAxis != null ? JToken.FromObject(SourceAxis) : null;

            settings[nameof(ConnectionKey)] = ProtectedStringUtils.Protect(ConnectionKey,
                e => Logger.Warn(e, "Failed to encrypt \"{0}\"", nameof(ConnectionKey)));
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DeviceAxis>(nameof(SourceAxis), out var sourceAxis))
                SourceAxis = sourceAxis;

            if (settings.TryGetValue<string>(nameof(ConnectionKey), out var encryptedConnectionKey))
                ConnectionKey = ProtectedStringUtils.Unprotect(encryptedConnectionKey,
                    e => Logger.Warn(e, "Failed to decrypt \"{0}\"", nameof(ConnectionKey)));
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region ConnectionKey
        s.RegisterAction<string>($"{Identifier}::ConnectionKey::Set", s => s.WithLabel("Connection key"), connectionKey => ConnectionKey = connectionKey);
        #endregion

        #region SourceAxis
        s.RegisterAction<DeviceAxis>($"{Identifier}::SourceAxis::Set", s => s.WithLabel("Source axis").WithItemsSource(DeviceAxis.All), axis => SourceAxis = axis);
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::ConnectionKey::Set");
        s.UnregisterAction($"{Identifier}::SourceAxis::Set");
    }
}
