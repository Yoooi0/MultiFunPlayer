using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class PipeOutputTargetViewModel : ThreadAbstractOutputTarget
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        public override string Name => "Pipe";
        public override OutputTargetStatus Status { get; protected set; }

        public string PipeName { get; set; } = "mfp-pipe";

        public PipeOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override void Run(CancellationToken token)
        {
            var client = default(NamedPipeClientStream);

            try
            {
                Logger.Info("Connecting to {0}", PipeName);

                client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2500);

                Status = OutputTargetStatus.Connected;
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
                var sb = new StringBuilder(256);
                var buffer = new byte[256];
                while (!token.IsCancellationRequested && client?.IsConnected == true)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    sb.Clear();
                    foreach (var (axis, value) in Values)
                    {
                        if (sb.Length > 0)
                            sb.Append(' ');

                        sb.Append(axis)
                          .AppendFormat("{0:000}", value * 999)
                          .AppendFormat("I{0}", (int)interval);
                    }
                    sb.AppendLine();

                    var commands = sb.ToString();
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
                Logger.Error(e, "Unhandled error");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error:\n\n{e}")));
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
                if (settings.TryGetValue(nameof(PipeName), out var pipeNameToken))
                    PipeName = pipeNameToken.ToObject<string>();
            }
        }
    }
}
