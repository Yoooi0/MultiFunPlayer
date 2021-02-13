using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
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
            : base(eventAggregator, valueProvider)
        {
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
        }

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override void Run(CancellationToken token)
        {
        }
    }
}
