using MultiFunPlayer.Common;
using PropertyChanged;
using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for UriBox.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
public sealed partial class UriBox : UserControl
{
    private static readonly EndPoint DefaultEndPoint = new IPEndPoint(IPAddress.Parse(DefaultHostOrIPAddress), DefaultPort);
    private const string DefaultScheme = "http";
    private const string DefaultHostOrIPAddress = "127.0.0.1";
    private const int DefaultPort = 80;

    private int _guardUpdateDepth;
    private bool IsUpdating => _guardUpdateDepth > 0;

    public string[] AvailableSchemesList { get; private set; } = null;

    [DoNotNotify]
    public string AvailableSchemes
    {
        get => (string)GetValue(AvailableSchemesProperty);
        set => SetValue(AvailableSchemesProperty, value);
    }

    public static readonly DependencyProperty AvailableSchemesProperty =
        DependencyProperty.Register(nameof(AvailableSchemes), typeof(string),
            typeof(UriBox), new FrameworkPropertyMetadata(null,
                    new PropertyChangedCallback(OnAvailableSchemesPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnAvailableSchemesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateAvailableSchemes();
    }

    [DoNotNotify]
    public string Scheme
    {
        get => (string)GetValue(SchemeProperty);
        set => SetValue(SchemeProperty, value);
    }

    public static readonly DependencyProperty SchemeProperty =
        DependencyProperty.Register(nameof(Scheme), typeof(string),
            typeof(UriBox), new FrameworkPropertyMetadata(DefaultScheme,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnSchemePropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnSchemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateUri();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [DoNotNotify]
    public string HostOrIPAddress
    {
        get => (string)GetValue(HostOrIPAddressProperty);
        set => SetValue(HostOrIPAddressProperty, value);
    }

    public static readonly DependencyProperty HostOrIPAddressProperty =
        DependencyProperty.Register(nameof(HostOrIPAddress), typeof(string),
            typeof(UriBox), new FrameworkPropertyMetadata(DefaultHostOrIPAddress,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnHostOrIPAddressyPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnHostOrIPAddressyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateUri();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [DoNotNotify]
    public int Port
    {
        get => (int)GetValue(PortProperty);
        set => SetValue(PortProperty, value);
    }

    public static readonly DependencyProperty PortProperty =
        DependencyProperty.Register(nameof(Port), typeof(int),
            typeof(UriBox), new FrameworkPropertyMetadata(DefaultPort,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnPortPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPortPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateUri();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [DoNotNotify]
    public EndPoint EndPoint
    {
        get => (EndPoint)GetValue(EndPointProperty);
        set => SetValue(EndPointProperty, value);
    }

    public static readonly DependencyProperty EndPointProperty =
        DependencyProperty.Register(nameof(EndPoint), typeof(EndPoint),
            typeof(UriBox), new FrameworkPropertyMetadata(DefaultEndPoint,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnEndPointPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnEndPointPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateHostOrIPAddressAndPort();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [DoNotNotify]
    public string PathAndQuery
    {
        get => (string)GetValue(PathAndQueryProperty);
        set => SetValue(PathAndQueryProperty, value);
    }

    public static readonly DependencyProperty PathAndQueryProperty =
        DependencyProperty.Register(nameof(PathAndQuery), typeof(string),
            typeof(UriBox), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnPathAndQueryPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPathAndQueryPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateUri();
    }

    [DoNotNotify]
    public bool ShowPathAndQuery
    {
        get => (bool)GetValue(ShowPathAndQueryProperty);
        set => SetValue(ShowPathAndQueryProperty, value);
    }

    public static readonly DependencyProperty ShowPathAndQueryProperty =
        DependencyProperty.Register(nameof(ShowPathAndQuery), typeof(bool),
            typeof(UriBox), new FrameworkPropertyMetadata(true));

    [DoNotNotify]
    public Uri Uri
    {
        get => (Uri)GetValue(UriProperty);
        set => SetValue(UriProperty, value);
    }

    public static readonly DependencyProperty UriProperty =
        DependencyProperty.Register(nameof(Uri), typeof(Uri),
            typeof(UriBox), new FrameworkPropertyMetadata(new Uri("http://127.0.0.1:80"),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnUriPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;
        if (@this.IsUpdating)
            return;

        @this.UpdateFromUri();
    }

    public UriBox()
    {
        InitializeComponent();
    }

    private void GuardUpdate(Action action)
    {
        _guardUpdateDepth++;
        action();
        _guardUpdateDepth--;
    }

    private void UpdateAvailableSchemes()
    {
        if (AvailableSchemes == null)
        {
            AvailableSchemesList = null;
            Scheme = DefaultScheme;
        }
        else
        {
            AvailableSchemesList = [.. AvailableSchemes.Split(',', ' ')];
            Scheme = AvailableSchemesList[0];
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scheme)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableSchemes)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableSchemesList)));
    }

    private void UpdateHostOrIPAddressAndPort()
    {
        GuardUpdate(() =>
        {
            if (EndPoint is IPEndPoint ipEndPoint)
            {
                SetValue(HostOrIPAddressProperty, ipEndPoint.Address.ToString());
                SetValue(PortProperty, ipEndPoint.Port);
            }
            else if (EndPoint is DnsEndPoint dnsEndPoint)
            {
                SetValue(HostOrIPAddressProperty, dnsEndPoint.Host);
                SetValue(PortProperty, dnsEndPoint.Port);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HostOrIPAddress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Port)));
            UpdateUri();
        });
    }

    private void UpdateUri()
    {
        GuardUpdate(() =>
        {
            if (string.IsNullOrWhiteSpace(HostOrIPAddress))
                SetValue(HostOrIPAddressProperty, DefaultHostOrIPAddress);

            if (!Uri.TryCreate($"{Scheme}://{HostOrIPAddress}:{Port}{PathAndQuery}", UriKind.Absolute, out var uri))
            {
                var scheme = AvailableSchemesList == null ? DefaultScheme : AvailableSchemesList[0];
                uri = new Uri($"{scheme}://{DefaultHostOrIPAddress}:{DefaultPort}", UriKind.Absolute);
            }

            SetValue(UriProperty, uri);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Uri)));

            UpdateFromUri();
        });
    }

    private void UpdateFromUri()
    {
        GuardUpdate(() =>
        {
            SetValue(SchemeProperty, Uri.Scheme);
            SetValue(HostOrIPAddressProperty, Uri.Host);
            SetValue(PortProperty, Uri.Port);
            SetValue(PathAndQueryProperty, Uri.PathAndQuery);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scheme)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HostOrIPAddress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Port)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PathAndQuery)));

            var type = Uri.CheckHostName(HostOrIPAddress);
            var endpoint = type switch
            {
                UriHostNameType.IPv4 or UriHostNameType.IPv6 when IPAddress.TryParse(HostOrIPAddress, out var ipAddress) => new IPEndPoint(ipAddress, Port),
                UriHostNameType.Dns => new DnsEndPoint(HostOrIPAddress, Port),
                _ => default(EndPoint)
            };

            SetValue(EndPointProperty, endpoint);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EndPoint)));
        });
    }
}
