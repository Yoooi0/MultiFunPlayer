using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class PipeOutputTargetViewModel : ThreadAbstractOutputTarget
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        public override string Name => "Pipe";
        public override ConnectionStatus Status { get; protected set; }

        public string PipeName { get; set; } = "mfp-pipe";

        public PipeOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

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

                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Error when opening pipe:\n\n{e}")));
                return;
            }

            try
            {
                var buffer = new byte[256];
                while (!token.IsCancellationRequested && client?.IsConnected == true)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    var commands = TCode.ToString(Values, (int)interval);
                    if (client?.IsConnected == true && !string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), PipeName);
                        var encoded = Encoding.ASCII.GetBytes(commands, buffer);
                        client?.Write(buffer, 0, encoded);
                    }

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"{Name} failed with exception");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"{Name} failed with exception:\n\n{e}")));
            }

            if (client?.IsConnected == true)
                client.Close();
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
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
}
