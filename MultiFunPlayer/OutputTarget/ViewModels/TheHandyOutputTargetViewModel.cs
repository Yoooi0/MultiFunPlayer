using Google.Protobuf.WellKnownTypes;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("The Handy")]
public class TheHandyOutputTargetViewModel : AsyncAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public string ConnectionKey { get; set; }
    public DeviceAxis SourceAxis { get; set; }

    public override ConnectionStatus Status { get; protected set; }

    public TheHandyOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        UpdateInterval = 100;
    }

    public override int MinimumUpdateInterval => 16;
    public override int MaximumUpdateInterval => 200;

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        using var client = NetUtils.CreateHttpClient();

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, ConnectionKey);

            if (string.IsNullOrWhiteSpace(ConnectionKey))
                throw new Exception("Invalid connection key");

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
                if (!response.TryGetValue<int>("mode", out var mode) || !response.TryGetValue<int>("result", out var result) || result != 0 || mode != 2)
                    throw new Exception($"Unable to set HDSP device mode [Response: {response.ToString(Formatting.None)}]");
            }

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when connecting to the device");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to the device", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var lastSentValue = double.NaN;
            await FixedUpdateAsync(() => !token.IsCancellationRequested, async elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                if (SourceAxis == null)
                    return;

                var currentValue = Values[SourceAxis];
                if (!double.IsFinite(lastSentValue) || Math.Abs(lastSentValue - currentValue) >= 0.005)
                {
                    var position = MathUtils.Clamp(currentValue * 100, 0, 100);
                    var duration = (int)Math.Floor(elapsed * 1000 + 0.75);

                    _ = await ApiPutAsync(client, "hdsp/xpt", $"{{ \"stopOnTarget\": false, \"duration\": {duration}, \"position\": {position.ToString(CultureInfo.InvariantCulture)} }}", token);
                    lastSentValue = currentValue;
                }
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

        Logger.Trace("{0} api response: {1}", Identifier, response.ToString(Formatting.None));
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
            settings[nameof(ConnectionKey)] = new JValue(ConnectionKey);
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
        s.RegisterAction($"{Identifier}::ConnectionKey::Set", b => b.WithSetting<string>(s => s.WithLabel("Connection key"))
                                                                    .WithCallback((_, connectionKey) => ConnectionKey = connectionKey));
        #endregion

        #region SourceAxis
        s.RegisterAction($"{Identifier}::SourceAxis::Set", b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Source axis"))
                                                                 .WithCallback((_, axis) => SourceAxis = axis));
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
