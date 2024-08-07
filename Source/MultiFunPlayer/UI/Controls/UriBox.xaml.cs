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
    private int _isUpdating;

    public IReadOnlyList<string> AvailableSchemes { get; private set; } = null;

    [OnChangedMethod(nameof(UpdateUri))]
    public string Scheme { get; set; }

    [OnChangedMethod(nameof(UpdateUri))]
    public string HostOrIPAddress { get; set; }

    [OnChangedMethod(nameof(UpdateUri))]
    public int Port { get; set; }

    [OnChangedMethod(nameof(UpdateUri))]
    public string PathAndQuery { get; set; }

    [DoNotNotify]
    public string Schemes
    {
        get => (string)GetValue(SchemesProperty);
        set => SetValue(SchemesProperty, value);
    }

    public static readonly DependencyProperty SchemesProperty =
        DependencyProperty.Register(nameof(Schemes), typeof(string),
            typeof(UriBox), new FrameworkPropertyMetadata(null,
                    new PropertyChangedCallback(OnAvailableSchemesPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnAvailableSchemesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UriBox @this)
            @this.UpdateAvailableSchemes();
    }

    [DoNotNotify]
    public bool ShowPort
    {
        get => (bool)GetValue(ShowPortProperty);
        set => SetValue(ShowPortProperty, value);
    }

    public static readonly DependencyProperty ShowPortProperty =
        DependencyProperty.Register(nameof(ShowPort), typeof(bool),
            typeof(UriBox), new FrameworkPropertyMetadata(true));

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
    public bool CanEditPort
    {
        get => (bool)GetValue(CanEditPortProperty);
        set => SetValue(CanEditPortProperty, value);
    }

    public static readonly DependencyProperty CanEditPortProperty =
        DependencyProperty.Register(nameof(CanEditPort), typeof(bool),
            typeof(UriBox), new FrameworkPropertyMetadata(true));

    [DoNotNotify]
    public bool CanEditPathAndQuery
    {
        get => (bool)GetValue(CanEditPathAndQueryProperty);
        set => SetValue(CanEditPathAndQueryProperty, value);
    }

    public static readonly DependencyProperty CanEditPathAndQueryProperty =
        DependencyProperty.Register(nameof(CanEditPathAndQuery), typeof(bool),
            typeof(UriBox), new FrameworkPropertyMetadata(true));

    [DoNotNotify]
    public Uri Uri
    {
        get => (Uri)GetValue(UriProperty);
        set => SetValue(UriProperty, value);
    }

    public static readonly DependencyProperty UriProperty =
        DependencyProperty.Register(nameof(Uri), typeof(Uri),
            typeof(UriBox), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnUriPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UriBox @this)
            return;

        @this.UpdateFromUri();
    }

    public UriBox()
    {
        InitializeComponent();
    }

    private void GuardUpdate(Action action)
    {
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
            return;

        action();
        _isUpdating = 0;
    }

    private void UpdateAvailableSchemes()
    {
        if (string.IsNullOrEmpty(Schemes))
        {
            AvailableSchemes = null;
            Scheme = null;
        }
        else
        {
            AvailableSchemes = [.. Schemes.Split((char[])[',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
            Scheme = AvailableSchemes.FirstOrDefault();
        }
    }

    private void UpdateUri()
    {
        GuardUpdate(() =>
        {
            if (PathAndQuery?.StartsWith('/') != true)
                PathAndQuery = $"/{PathAndQuery}";

            var validData = Scheme != null && HostOrIPAddress != null && Port > 0 && Port <= 65535;
            if (validData && Uri.TryCreate($"{Scheme}://{HostOrIPAddress}:{Port}{PathAndQuery}", UriKind.Absolute, out var uri))
                SetValue(UriProperty, uri);
            else
                SetValue(UriProperty, null);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Uri)));
            UpdateFromUri();
        });
    }

    private void UpdateFromUri()
    {
        GuardUpdate(() =>
        {
            Scheme = Uri?.Scheme;
            HostOrIPAddress = Uri?.Host;
            Port = Uri?.Port ?? 0;
            PathAndQuery = Uri?.PathAndQuery;
        });
    }
}
