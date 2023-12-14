using PropertyChanged;
using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for EndpointBox.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
public sealed partial class EndPointBox : UserControl
{
    [OnChangedMethod(nameof(UpdateEndPoint))]
    public string HostOrIPAddress { get; set; }
    [OnChangedMethod(nameof(UpdateEndPoint))]
    public int Port { get; set; }

    [DoNotNotify]
    public EndPoint EndPoint
    {
        get => (EndPoint)GetValue(EndPointProperty);
        set => SetValue(EndPointProperty, value);
    }

    public static readonly DependencyProperty EndPointProperty =
        DependencyProperty.Register(nameof(EndPoint), typeof(EndPoint),
            typeof(EndPointBox), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnEndPointPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnEndPointPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not EndPointBox @this)
            return;

        if(e.NewValue is IPEndPoint ipEndPoint)
        {
            @this.HostOrIPAddress = ipEndPoint.Address.ToString();
            @this.Port = ipEndPoint.Port;
        }
        else if(e.NewValue is DnsEndPoint dnsEndPoint)
        {
            @this.HostOrIPAddress = dnsEndPoint.Host;
            @this.Port = dnsEndPoint.Port;
        }

        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    public EndPointBox()
    {
        InitializeComponent();
    }

    private void UpdateEndPoint()
    {
        var type = Uri.CheckHostName(HostOrIPAddress);
        var endpoint = type switch
        {
            UriHostNameType.IPv4 or UriHostNameType.IPv6 when IPAddress.TryParse(HostOrIPAddress, out var ipAddress) => new IPEndPoint(ipAddress, Port),
            UriHostNameType.Dns => new DnsEndPoint(HostOrIPAddress, Port),
            _ => default(EndPoint)
        };

        SetValue(EndPointProperty, endpoint);
    }
}
