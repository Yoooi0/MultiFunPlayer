using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Pipe")]
internal sealed class PipeOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : ThreadAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public DeviceAxisUpdateType UpdateType { get; set; } = DeviceAxisUpdateType.FixedUpdate;
    public bool CanChangeUpdateType => !IsConnectBusy && !IsConnected;

    public string PipeName { get; set; } = "mfp-pipe";

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new TCodeThreadFixedUpdateContext(),
        DeviceAxisUpdateType.PolledUpdate => new ThreadPolledUpdateContext(),
        _ => null,
    };

    protected override void Run(CancellationToken token)
    {
        var client = default(NamedPipeClientStream);

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, PipeName);

            client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2500);

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error when opening pipe");
            if (client?.IsConnected == true)
                client.Close();

            _ = DialogHelper.ShowErrorAsync(e, "Error when opening pipe", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var buffer = new byte[256];
            if (UpdateType == DeviceAxisUpdateType.FixedUpdate)
            {
                var currentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
                var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
                FixedUpdate<TCodeThreadFixedUpdateContext>(() => !token.IsCancellationRequested && client?.IsConnected == true, (context, elapsed) =>
                {
                    Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                    GetValues(currentValues);

                    var values = context.SendDirtyValuesOnly ? currentValues.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : currentValues;
                    values = values.Where(x => AxisSettings[x.Key].Enabled);

                    var commands = context.OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                    if (client.IsConnected && !string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), PipeName);

                        var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                        client.Write(buffer, 0, encoded);
                        lastSentValues.Merge(values);
                    }
                });
            }
            else if (UpdateType == DeviceAxisUpdateType.PolledUpdate)
            {
                PolledUpdate(DeviceAxis.All, () => !token.IsCancellationRequested, (_, axis, snapshot, elapsed) =>
                {
                    Logger.Trace("Begin PolledUpdate [Axis: {0}, Index From: {1}, Index To: {2}, Duration: {3}, Elapsed: {4}]",
                        axis, snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);

                    var settings = AxisSettings[axis];
                    if (!settings.Enabled)
                        return;
                    if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                        return;

                    var value = MathUtils.Lerp(settings.Minimum / 100, settings.Maximum / 100, snapshot.KeyframeTo.Value);
                    var duration = snapshot.Duration;

                    var command = DeviceAxis.ToString(axis, value, duration * 1000);
                    if (client.IsConnected && !string.IsNullOrWhiteSpace(command))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", command, PipeName);

                        var encoded = Encoding.UTF8.GetBytes($"{command}\n", buffer);
                        client.Write(buffer, 0, encoded);
                    }
                }, token);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }

        try
        {
            if (client?.IsConnected == true)
                client.Close();
        } catch { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(UpdateType)] = JToken.FromObject(UpdateType);
            settings[nameof(PipeName)] = PipeName;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DeviceAxisUpdateType>(nameof(UpdateType), out var updateType))
                UpdateType = updateType;
            if (settings.TryGetValue<string>(nameof(PipeName), out var pipeName))
                PipeName = pipeName;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region PipeName
        s.RegisterAction<string>($"{Identifier}::PipeName::Set", s => s.WithLabel("Pipe name"), pipeName => PipeName = pipeName);
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::PipeName::Set");
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            return await ValueTask.FromResult(File.Exists($@"\\.\pipe\{PipeName}"));
        }
        catch
        {
            return await ValueTask.FromResult(false);
        }
    }
}
