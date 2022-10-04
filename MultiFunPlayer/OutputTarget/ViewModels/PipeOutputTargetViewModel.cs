using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
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
public class PipeOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public bool OffloadElapsedTime { get; set; } = true;
    public bool SendDirtyValuesOnly { get; set; } = true;
    public string PipeName { get; set; } = "mfp-pipe";

    public PipeOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider) { }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

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
            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate(() => !token.IsCancellationRequested && client?.IsConnected == true, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                var values = SendDirtyValuesOnly ? Values.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : Values;
                var commands = OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                if (client.IsConnected && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), PipeName);
                    var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                    client.Write(buffer, 0, encoded);
                    lastSentValues.Merge(values);
                }
            });
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
            settings[nameof(PipeName)] = PipeName;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(PipeName), out var pipeName))
                PipeName = pipeName;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region PipeName
        s.RegisterAction($"{Identifier}::PipeName::Set", b => b.WithSetting<string>(s => s.WithLabel("Pipe name")).WithCallback((_, pipeName) => PipeName = pipeName));
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
