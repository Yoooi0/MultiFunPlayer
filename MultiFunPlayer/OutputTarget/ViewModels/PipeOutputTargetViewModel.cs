using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Pipe")]
public class PipeOutputTargetViewModel : ThreadAbstractOutputTarget
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public string PipeName { get; set; } = "mfp-pipe";

    public PipeOutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(shortcutManager, eventAggregator, valueProvider) { }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override void Run(CancellationToken token)
    {
        var client = default(NamedPipeClientStream);

        try
        {
            Logger.Info("Connecting to {0}", PipeName);

            client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2500);

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when opening pipe");
            if (client?.IsConnected == true)
                client.Close();

            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"Error when opening pipe:\n\n{e}"), "RootDialog"));
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var buffer = new byte[256];
            var stopwatch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && client?.IsConnected == true)
            {
                UpdateValues();

                var commands = DeviceAxis.ToString(Values, UpdateInterval);
                if (client?.IsConnected == true && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), PipeName);
                    var encoded = Encoding.ASCII.GetBytes(commands, buffer);
                    client?.Write(buffer, 0, encoded);
                }

                Sleep(stopwatch);
                stopwatch.Restart();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} failed with exception:\n\n{e}"), "RootDialog"));
        }

        if (client?.IsConnected == true)
            client.Close();
    }

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
    {
        base.HandleSettings(settings, type);

        if (type == AppSettingsMessageType.Saving)
        {
            if (PipeName != null)
                settings[nameof(PipeName)] = new JValue(PipeName);
        }
        else if (type == AppSettingsMessageType.Loading)
        {
            if (settings.TryGetValue<string>(nameof(PipeName), out var pipeName))
                PipeName = pipeName;
        }
    }

    protected override void RegisterShortcuts(IShortcutManager s)
    {
        base.RegisterShortcuts(s);

        #region PipeName
        s.RegisterAction($"{Name}::PipeName::Set", b => b.WithSetting<string>(s => s.WithLabel("Pipe name")).WithCallback((_, pipeName) => PipeName = pipeName));
        #endregion
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
