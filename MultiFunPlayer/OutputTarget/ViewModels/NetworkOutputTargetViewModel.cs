using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public enum ProtocolType
    {
        Tcp,
        Udp
    }

    public class NetworkOutputTargetViewModel : ThreadAbstractOutputTarget
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        public override string Name => "Network";
        public override ConnectionStatus Status { get; protected set; }

        public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);
        public ProtocolType Protocol { get; set; } = ProtocolType.Tcp;

        public NetworkOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

        public bool IsConnected => Status == ConnectionStatus.Connected;
        public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override void Run(CancellationToken token)
        {
            if (Endpoint == null)
                return;

            if (Protocol == ProtocolType.Tcp)
                RunTcp(token);
            else if (Protocol == ProtocolType.Udp)
                RunUdp(token);
        }

        private void RunTcp(CancellationToken token)
        {
            using var client = new TcpClient();

            try
            {
                Logger.Info("Connecting to {0}", $"tcp://{Endpoint}");
                client.Connect(Endpoint);
                Status = ConnectionStatus.Connected;
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Error when connecting to server");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Error when connecting to server:\n\n{e}")));
                return;
            }

            try
            {
                using var stream = new StreamWriter(client.GetStream(), Encoding.ASCII);
                while (!token.IsCancellationRequested && client?.Connected == true)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    var commands = TCode.ToString(Values, (int)interval);
                    if (client.Connected && !string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"tcp://{Endpoint}");
                        stream.WriteLine(commands);
                    }

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Unhandled error");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error:\n\n{e}")));
            }
        }

        private void RunUdp(CancellationToken token)
        {
            using var client = new UdpClient();

            try
            {
                Logger.Info("Connecting to {0}", $"udp://{Endpoint}");
                client.Connect(Endpoint);
                Status = ConnectionStatus.Connected;
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Error when connecting to server");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Error when connecting to server:\n\n{e}")));
                return;
            }

            try
            {
                var sb = new StringBuilder(256);
                var buffer = new byte[256];
                while (!token.IsCancellationRequested)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    var commands = TCode.ToString(Values, (int)interval);
                    if (!string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"udp://{Endpoint}");

                        var encoded = Encoding.ASCII.GetBytes(commands, buffer);
                        client.Send(buffer, encoded);
                    }

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"{Name} failed with exception");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"{Name} failed with exception:\n\n{e}")));
            }
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
            if (type == AppSettingsMessageType.Saving)
            {
                if(Endpoint != null)
                    settings[nameof(Endpoint)] = new JValue(Endpoint.ToString());

                settings[nameof(Protocol)] = new JValue(Protocol.ToString());
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue<string>(nameof(Endpoint), out var endpointString) && IPEndPoint.TryParse(endpointString, out var endpoint))
                    Endpoint = endpoint;

                if (settings.TryGetValue<ProtocolType>(nameof(Protocol), out var protocol))
                    Protocol = protocol;
            }
        }

        public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
        {
            try
            {
                if (Protocol == ProtocolType.Udp)
                    return await ValueTask.FromResult(true);

                using var client = new TcpClient();
                client.Connect(Endpoint);
                client.GetStream();
                return await ValueTask.FromResult(true);
            }
            catch
            {
                return await ValueTask.FromResult(false);
            }
        }
    }
}
