using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
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
internal class TheHandyOutputTargetViewModel : AsyncAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public string ConnectionKey { get; set; } = null;
    public DeviceAxis SourceAxis { get; set; } = null;

    public override ConnectionStatus Status { get; protected set; }

    public TheHandyOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        UpdateInterval = 100;

        PropertyChanged += (s, e) =>
        {
            if (Status != ConnectionStatus.Connected || e.PropertyName != nameof(SourceAxis) || SourceAxis == null)
                return;

            EventAggregator.Publish(new SyncRequestMessage(SourceAxis));
        };
    }

    public override int MinimumUpdateInterval => 16;
    public override int MaximumUpdateInterval => 200;

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SourceAxis != null;

    protected override async Task RunAsync(CancellationToken token)
    {
        using var client = NetUtils.CreateHttpClient();

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, ConnectionKey);

            if (string.IsNullOrWhiteSpace(ConnectionKey))
                throw new Exception("Invalid connection key");
            if (SourceAxis == null)
                throw new Exception("Source axis not selected");

            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Connection-Key", ConnectionKey);

            {
                var response = await ApiGetAsync(client, "connected", token);
                if (!response.TryGetValue<bool>("connected", out var connected) || !connected)
                    throw new Exception("Device is not connected");
            }

            {
                var response = await ApiGetAsync(client, "info", token);
                if (!response.TryGetValue<int>("fwStatus", out var firmwareStatus) || firmwareStatus == 1)
                    throw new Exception("Out of date firmware version, update required");
            }

            {
                var response = await ApiPutAsync(client, "mode", "{ \"mode\": 2 }", token);
                if (!response.TryGetValue<int>("result", out var result) || result == -1)
                    throw new Exception($"Unable to set HDSP device mode [Response: {response.ToString(Formatting.None)}]");
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error when connecting to the device");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to the device", "RootDialog");
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

            await PolledUpdateAsync(SourceAxis, () => !token.IsCancellationRequested, async (snapshot, elapsed) =>
            {
                Logger.Trace("Begin PolledUpdate [Index From: {0}, Index To: {1}, Duration: {2}, Elapsed: {3}]", snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);
                if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                    return;

                if (!AxisSettings[SourceAxis].Enabled)
                    return;

                var position = Math.Clamp(snapshot.KeyframeTo.Value * 100, 0, 100);
                var duration = (int)Math.Floor(snapshot.Duration * 1000 + 0.75);
                var content = $"{{ \"immediateResponse\": true, \"stopOnTarget\": true, \"duration\": {duration}, \"position\": {position.ToString(CultureInfo.InvariantCulture)} }}";

                _ = await ApiPutAsync(client, "hdsp/xpt", content, token);
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
            throw new Exception($"Api call failed: {error.ToString(Formatting.None)}");

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
            settings[nameof(ConnectionKey)] = ConnectionKey;
            settings[nameof(SourceAxis)] = SourceAxis != null ? JToken.FromObject(SourceAxis) : null;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(ConnectionKey), out var connectionKey))
                ConnectionKey = connectionKey;
            if (settings.TryGetValue<DeviceAxis>(nameof(SourceAxis), out var sourceAxis))
                SourceAxis = sourceAxis;
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

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ConnectionKey))
                return false;

            using var client = NetUtils.CreateHttpClient();

            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Connection-Key", ConnectionKey);

            var response = await ApiGetAsync(client, "connected", token);
            return response.TryGetValue<bool>("connected", out var connected) && connected;
        }
        catch
        {
            return false;
        }
    }
}
