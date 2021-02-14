using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class NetworkOutputTargetViewModel : AbstractOutputTarget
    {
        public override string Name => "Network";
        public override OutputTargetStatus Status { get; protected set; }

        public string Address { get; set; } = "localhost";
        public int Port { get; set; } = 8080;

        public NetworkOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override void Run(CancellationToken token)
        {
            using var client = new TcpClient();

            try
            {
                client.Connect(Address, Port);

                Status = OutputTargetStatus.Connected;
            }
            catch (Exception e)
            {
                if (client?.Connected == true)
                    client.Close();

                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Error when connecting to server:\n\n{e}")));
                return;
            }

            try
            {
                var sb = new StringBuilder(256);
                using var stream = new StreamWriter(client.GetStream(), Encoding.ASCII);
                while (!token.IsCancellationRequested)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    sb.Clear();
                    foreach (var (axis, value) in Values)
                    {
                        sb.Append(axis)
                          .AppendFormat("{0:000}", value * 999)
                          .AppendFormat("I{0}", (int)interval)
                          .Append(' ');
                    }

                    var commands = sb.ToString().Trim();
                    if (client?.Connected == true && !string.IsNullOrWhiteSpace(commands))
                        stream.WriteLine(commands);

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e)
            {
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error:\n\n{e}")));
            }

            if (client?.Connected == true)
                client.Close();
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
            if (type == AppSettingsMessageType.Saving)
            {
                settings[nameof(Address)] = new JValue(Address);
                settings[nameof(Port)] = new JValue(Port);
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue(nameof(Address), out var addressToken))
                    Address = addressToken.ToObject<string>();

                if (settings.TryGetValue(nameof(Port), out var portToken))
                    Port = portToken.ToObject<int>();
            }
        }
    }
}
